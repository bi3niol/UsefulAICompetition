using Azure.AI.Projects;
using Azure.Identity;
using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Sender;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Writer;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.ClientModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App;

public class ImpactAnalysisPipeline
{
    private readonly AIProjectClient _projectClient;
    private readonly ResearchTools _researchTools;
    private readonly WriterTools _writerTools;
    private readonly SenderTools _senderTools;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImpactAnalysisPipeline> _logger;

    private const int MaxEditorRetries = 2;
    private const int MaxRateLimitRetries = 6;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);

    public ImpactAnalysisPipeline(
        AIProjectClient projectClient,
        ResearchTools researchTools,
        WriterTools writerTools,
        SenderTools senderTools,
        IConfiguration configuration,
        ILogger<ImpactAnalysisPipeline> logger)
    {
        _projectClient = projectClient;
        _researchTools = researchTools;
        _writerTools = writerTools;
        _senderTools = senderTools;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> RunAsync(WorkItemEvent workItem)
    {
        var isBug = workItem.Type.Equals("Bug", StringComparison.OrdinalIgnoreCase);
        _logger.LogInformation("[PIPELINE] Starting analysis for WI#{WorkItemId} (Type: {Type})", workItem.Id, workItem.Type);

        // KROK 1: Researcher zbiera dane
        var findings = await RunResearcherAsync(workItem, isBug);

        // KROK 2 + 3: Writer pisze › Editor ocenia (z mo¿liwoœci¹ iteracji)
        var approvedReport = await RunWriterEditorLoopAsync(workItem, findings, isBug);

        // KROK 4: Sender wy³¹czony — raport zwracany do wywo³uj¹cego
        _logger.LogInformation("[PIPELINE] Analysis complete for WI#{WorkItemId}", workItem.Id);

        return approvedReport;
    }

    // ¦¦ KROK 1 ¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦
    private async Task<ResearchFindings> RunResearcherAsync(WorkItemEvent workItem, bool isBug)
    {
        _logger.LogInformation("[RESEARCHER] Searching DevOps items and WIKI...");

        var researcherModel = _configuration["Pipeline:ResearcherModel"] ?? "o4-mini";
        var researcherPrompt = isBug ? AgentPrompts.BugResearcherPrompt : AgentPrompts.ResearcherPrompt;
        var researcher = _projectClient.AsAIAgent(
            model: researcherModel,
            instructions: researcherPrompt,
            tools: _researchTools.GetAll());

        var prompt = $"""
            New work item to analyze:
            ID: {workItem.Id}
            Type: {workItem.Type}
            Title: {workItem.Title}
            Description: {workItem.Description}
            Acceptance Criteria: {workItem.AcceptanceCriteria}
            Area: {workItem.AreaPath}
            Tags: {workItem.Tags}

            Perform comprehensive research. Return structured JSON findings.
        """;

        var result = await WithRateLimitRetryAsync(
            () => researcher.RunAsync<ResearchFindings>(prompt),
            "RESEARCHER");
        return result.Result;
    }

    // ¦¦ KROK 2 + 3 ¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦
    private async Task<string> RunWriterEditorLoopAsync(
        WorkItemEvent workItem,
        ResearchFindings findings,
        bool isBug)
    {
        var retries = 0;
        string report;
        EditorDecision? decision = null;

        do
        {
            // WRITER: produkuje raport
            _logger.LogInformation("[WRITER] Generating report (attempt {Attempt})...", retries + 1);
            report = await RunWriterAsync(workItem, findings, isBug,
                previousFeedback: decision?.Feedback);

            // EDITOR: ocenia raport
            _logger.LogInformation("[EDITOR] Reviewing report...");
            decision = await RunEditorAsync(workItem, findings, report, isBug);

            if (!decision.IsApproved)
                _logger.LogWarning("[EDITOR] Rejected. Feedback: {Feedback}", decision.Feedback);

            retries++;

        } while (!decision.IsApproved && retries < MaxEditorRetries);

        if (!decision.IsApproved)
        {
            // Po max retry — u¿yj ostatniego raportu z adnotacj¹
            _logger.LogWarning("[EDITOR] Max retries reached, using last draft.");
            report = $"> ?? Note: Report may be incomplete.\n\n{report}";
        }

        return report;
    }

    private async Task<string> RunWriterAsync(
        WorkItemEvent workItem,
        ResearchFindings findings,
        bool isBug,
        string? previousFeedback)
    {
        var writerModel = _configuration["Pipeline:WriterModel"] ?? "o4-mini";
        var writerPrompt = isBug ? AgentPrompts.BugWriterPrompt : AgentPrompts.WriterPrompt;
        var writer = _projectClient.AsAIAgent(
            model: writerModel,
            instructions: writerPrompt,
            tools: _writerTools.GetAll());

        var feedbackSection = previousFeedback != null
            ? $"\n\n## Editor Feedback to Address:\n{previousFeedback}"
            : string.Empty;

        var prompt = $"""
            Write {(isBug ? "a bug diagnosis report" : "an impact analysis report")} for:
            Title: {workItem.Title} (#{workItem.Id})
            Type: {workItem.Type}

            Research findings:
            {JsonSerializer.Serialize(findings)}
            {feedbackSection}

            Produce a complete markdown report.
        """;

        return (await WithRateLimitRetryAsync(
            () => writer.RunAsync<string>(prompt),
            "WRITER")).Result;
    }

    private async Task<EditorDecision> RunEditorAsync(
        WorkItemEvent workItem,
        ResearchFindings findings,
        string draftReport,
        bool isBug)
    {
        var editorModel = _configuration["Pipeline:EditorModel"] ?? "gpt-4o";
        var editorPrompt = isBug ? AgentPrompts.BugEditorPrompt : AgentPrompts.EditorPrompt;
        var editor = _projectClient.AsAIAgent(
            model: editorModel,
            instructions: editorPrompt);

        var prompt = $$"""
            Review this {{(isBug ? "bug diagnosis" : "impact analysis")}} report for WI#{{workItem.Id}}.

            Original work item:
            Title: {{workItem.Title}}
            Description: {{workItem.Description}}

            Research findings available:
            {{JsonSerializer.Serialize(findings)}}

            Draft report to review:
            {{draftReport}}

            Return JSON: { "isApproved": bool, "feedback": "string or null" }
        """;

        var result = await WithRateLimitRetryAsync(
            () => editor.RunAsync<EditorDecision>(prompt),
            "EDITOR");
        return result.Result;
    }

    // ¦¦ KROK 4 ¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦
    private async Task RunSenderAsync(int workItemId, string report)
    {
        _logger.LogInformation("[SENDER] Posting report to WI#{WorkItemId}...", workItemId);

        var senderModel = _configuration["Pipeline:SenderModel"] ?? "gpt-4o";
        var sender = _projectClient.AsAIAgent(
            model: senderModel,
            instructions: AgentPrompts.SenderPrompt,
            tools: _senderTools.GetAll());

        var prompt = $"""
            Post this approved impact analysis report as a comment 
            on work item #{workItemId}:

            {report}
        """;

        await WithRateLimitRetryAsync(
            async () => { await sender.RunAsync(prompt); return 0; },
            "SENDER");
    }

    private async Task<T> WithRateLimitRetryAsync<T>(
        Func<Task<T>> action, string agentName)
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