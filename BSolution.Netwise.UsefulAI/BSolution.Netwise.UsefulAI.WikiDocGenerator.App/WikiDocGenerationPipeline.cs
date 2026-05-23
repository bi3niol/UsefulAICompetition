using Azure.AI.Projects;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Sender;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.ClientModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App;

/// <summary>
/// 4-agent pipeline generujący / aktualizujący dokumentację wiki:
///   Researcher → Writer → Editor (z pętlą poprawek) → Sender.
///
/// Wzorowany na <c>ImpactAnalysisPipeline</c> z Impact Analyzera, ale:
///   - operuje na PR / Feature, nie na pojedynczym work item incydencie,
///   - pisze do OSOBNEGO wiki (TargetWikiId) zamiast komentować work item,
///   - Researcher czyta repo i obecne strony wiki, nie indeksu AI Search.
/// </summary>
public class WikiDocGenerationPipeline
{
    private readonly AIProjectClient _projectClient;
    private readonly ResearchTools _researchTools;
    private readonly SenderTools _senderTools;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WikiDocGenerationPipeline> _logger;

    private const int MaxEditorRetries = 2;
    private const int MaxRateLimitRetries = 6;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);

    public WikiDocGenerationPipeline(
        AIProjectClient projectClient,
        ResearchTools researchTools,
        SenderTools senderTools,
        IConfiguration configuration,
        ILogger<WikiDocGenerationPipeline> logger)
    {
        _projectClient = projectClient;
        _researchTools = researchTools;
        _senderTools = senderTools;
        _configuration = configuration;
        _logger = logger;
    }

    // ── Public entry points ────────────────────────────────────────────────

    public async Task<string> RunForPullRequestAsync(PullRequestEvent pr, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[WIKI-PIPELINE] Starting wiki generation for PR#{Id} in {Repo} ({Source} -> {Target})",
            pr.PullRequestId, pr.RepositoryName, pr.SourceBranch, pr.TargetBranch);

        var seedPrompt = BuildSeedPromptForPullRequest(pr);
        return await RunInternalAsync(seedPrompt, ct);
    }

    public async Task<string> RunForWorkItemsAsync(WorkItemsWikiRefreshRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[WIKI-PIPELINE] Starting wiki refresh for {Count} work item(s): [{Ids}]",
            request.WorkItemIds.Count, string.Join(", ", request.WorkItemIds));

        var seedPrompt = BuildSeedPromptForWorkItems(request);
        return await RunInternalAsync(seedPrompt, ct);
    }

    // ── Pipeline core ──────────────────────────────────────────────────────

    private async Task<string> RunInternalAsync(string seedPrompt, CancellationToken ct)
    {
        var findings = await RunResearcherAsync(seedPrompt);
        var draft = await RunWriterEditorLoopAsync(findings);
        await RunSenderAsync(draft);

        _logger.LogInformation(
            "[WIKI-PIPELINE] Done. Pages written: {Count}. Summary: {Summary}",
            draft.Edits.Count, draft.Summary);

        return JsonSerializer.Serialize(new
        {
            summary = draft.Summary,
            edits = draft.Edits.Select(e => new { e.Path, e.Rationale })
        });
    }

    // ── Public stage methods (used by stage Functions) ──────────────────────

    /// <summary>Stage 2: Build seed prompt from message data, run Researcher, return findings.</summary>
    public string BuildSeedPromptForPullRequest(PullRequestEvent pr) => $"""
        Source: PULL_REQUEST
        RepositoryId: {pr.RepositoryId}
        RepositoryName: {pr.RepositoryName}
        PullRequestId: {pr.PullRequestId}
        Title: {pr.Title}
        Description: {pr.Description}
        SourceBranch: {pr.SourceBranch}
        TargetBranch: {pr.TargetBranch}
        MergeCommitId: {pr.MergeCommitId}
        LinkedWorkItemIds: [{string.Join(", ", pr.LinkedWorkItemIds)}]

        Research which wiki pages need to be created or updated to reflect this PR.
    """;

    /// <summary>Stage 2: Build seed prompt for work items batch.</summary>
    public string BuildSeedPromptForWorkItems(WorkItemsWikiRefreshRequest request) => $"""
        Source: WORK_ITEMS
        WorkItemIds: [{string.Join(", ", request.WorkItemIds)}]
        RepositoryId: {request.RepositoryId}

        For EACH work item:
          1. Fetch its details (description, acceptance criteria, comments) via
             GetWorkItemDetails() to understand the feature intent.
          2. Decide whether the work item describes the SAME topic as another
             already-processed work item — group such items together so they
             contribute to the SAME wiki page instead of producing duplicates.

        Then for each topic (group of related work items):
          • Search existing wiki pages with ListWikiPages() and pick the page
            whose path / title best matches the topic. Read its current content
            with GetWikiPage() and carry over the ETag.
          • Only when NO existing page reasonably covers the topic, propose a
            NEW page path under a sensible hierarchy.
          • If a repository id is provided, optionally read a few representative
            files with ReadRepositoryFile() to make the documentation concrete.

        Output the standard WikiResearchFindings JSON.
    """;

    /// <summary>Stage 2: Run Researcher agent with given seed prompt.</summary>
    public async Task<WikiResearchFindings> RunResearcherAsync(string seedPrompt)
    {
        _logger.LogInformation("[RESEARCHER] Investigating code, work items and existing wiki...");

        var model = _configuration["Pipeline:ResearcherModel"] ?? "o4-mini";
        var researcher = _projectClient.AsAIAgent(
            model: model,
            instructions: WikiDocAgentPrompts.ResearcherPrompt,
            tools: _researchTools.GetAll());

        var result = await WithRateLimitRetryAsync(
            () => researcher.RunAsync<WikiResearchFindings>(seedPrompt),
            "RESEARCHER");
        return result.Result;
    }

    /// <summary>Stage 3: Run Writer + Editor loop, return approved draft.</summary>
    public async Task<WikiDraft> RunWriterEditorLoopAsync(WikiResearchFindings findings)
    {
        var retries = 0;
        WikiDraft draft;
        EditorDecision? decision = null;

        do
        {
            _logger.LogInformation("[WRITER] Producing wiki edits (attempt {Attempt})...", retries + 1);
            draft = await RunWriterAsync(findings, decision?.Feedback);

            _logger.LogInformation("[EDITOR] Reviewing draft of {Count} edit(s)...", draft.Edits.Count);
            decision = await RunEditorAsync(findings, draft);

            if (!decision.IsApproved)
                _logger.LogWarning("[EDITOR] Rejected. Feedback: {Feedback}", decision.Feedback);

            retries++;
        }
        while (!decision.IsApproved && retries < MaxEditorRetries);

        if (!decision.IsApproved)
            _logger.LogWarning("[EDITOR] Max retries reached, using last draft as-is.");

        return draft;
    }

    private async Task<WikiDraft> RunWriterAsync(WikiResearchFindings findings, string? previousFeedback)
    {
        var model = _configuration["Pipeline:WriterModel"] ?? "o4-mini";
        var writer = _projectClient.AsAIAgent(
            model: model,
            instructions: WikiDocAgentPrompts.WriterPrompt);

        var feedbackSection = previousFeedback != null
            ? $"\n\n## Editor Feedback to Address:\n{previousFeedback}"
            : string.Empty;

        var prompt = $"""
            Produce wiki edits for the following research findings.

            {JsonSerializer.Serialize(findings)}
            {feedbackSection}
        """;

        var result = await WithRateLimitRetryAsync(
            () => writer.RunAsync<WikiDraft>(prompt),
            "WRITER");
        return result.Result;
    }

    private async Task<EditorDecision> RunEditorAsync(WikiResearchFindings findings, WikiDraft draft)
    {
        var model = _configuration["Pipeline:EditorModel"] ?? "gpt-4o";
        var editor = _projectClient.AsAIAgent(
            model: model,
            instructions: WikiDocAgentPrompts.EditorPrompt);

        var prompt = $"""
            Review the following draft of wiki edits.

            Research findings:
            {JsonSerializer.Serialize(findings)}

            Writer draft:
            {JsonSerializer.Serialize(draft)}
        """;

        var result = await WithRateLimitRetryAsync(
            () => editor.RunAsync<EditorDecision>(prompt),
            "EDITOR");
        return result.Result;
    }

    // ── KROK 4: Sender ─────────────────────────────────────────────────────

    /// <summary>Stage 4: Apply wiki edits using Sender agent.</summary>
    public async Task RunSenderAsync(WikiDraft draft)
    {
        if (draft.Edits.Count == 0)
        {
            _logger.LogInformation("[SENDER] No edits to apply.");
            return;
        }

        _logger.LogInformation("[SENDER] Writing {Count} page(s) to target wiki...", draft.Edits.Count);

        var model = _configuration["Pipeline:SenderModel"] ?? "gpt-4o";
        var sender = _projectClient.AsAIAgent(
            model: model,
            instructions: WikiDocAgentPrompts.SenderPrompt,
            tools: _senderTools.GetAll());

        var prompt = $"""
            Apply each of the following wiki edits using UpsertWikiPage(). Do not modify content.

            {JsonSerializer.Serialize(draft.Edits)}
        """;

        await WithRateLimitRetryAsync(
            async () => { await sender.RunAsync(prompt); return 0; },
            "SENDER");
    }

    // ── Retry helper ───────────────────────────────────────────────────────

    private async Task<T> WithRateLimitRetryAsync<T>(Func<Task<T>> action, string agentName)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (ClientResultException ex) when (ex.Status == 429 && attempt <= MaxRateLimitRetries)
            {
                var delay = InitialRetryDelay * Math.Pow(2, attempt - 1);
                _logger.LogWarning(
                    "[{Agent}] Rate limited (429). Retry {Attempt}/{Max} after {Delay}s...",
                    agentName, attempt, MaxRateLimitRetries, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }
    }
}
