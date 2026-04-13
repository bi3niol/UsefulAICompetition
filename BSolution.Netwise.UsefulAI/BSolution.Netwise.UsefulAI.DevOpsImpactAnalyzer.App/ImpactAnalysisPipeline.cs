using Azure.AI.Projects;
using Azure.Identity;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Sender;
using Microsoft.Agents.AI;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App;

public class ImpactAnalysisPipeline
{
    private readonly AIAgent _researcher;
    private readonly AIAgent _writer;
    private readonly AIAgent _editor;
    private readonly AIAgent _sender;

    private const int MaxEditorRetries = 2; 

    public ImpactAnalysisPipeline(
        AIProjectClient projectClient,
        ResearchTools researchTools,
        SenderTools senderTools)
    {
        _researcher = projectClient.AsAIAgent(
            model: "gpt-4o",
            instructions: AgentPrompts.ResearcherPrompt,
            tools: researchTools.GetAll());

        _writer = projectClient.AsAIAgent(
            model: "gpt-4o",
            instructions: AgentPrompts.WriterPrompt);

        _editor = projectClient.AsAIAgent(
            model: "gpt-4o",
            instructions: AgentPrompts.EditorPrompt);

        _sender = projectClient.AsAIAgent(
            model: "gpt-4o",
            instructions: AgentPrompts.SenderPrompt,
            tools: senderTools.GetAll());
    }

    public async Task RunAsync(WorkItemEvent workItem)
    {
        Console.WriteLine($"[PIPELINE] Starting analysis for WI#{workItem.Id}");

        // KROK 1: Researcher zbiera dane
        var findings = await RunResearcherAsync(workItem);

        // KROK 2 + 3: Writer pisze → Editor ocenia (z możliwością iteracji)
        var approvedReport = await RunWriterEditorLoopAsync(workItem, findings);

        // KROK 4: Sender zapisuje wynik
        await RunSenderAsync(workItem.Id, approvedReport);

        Console.WriteLine($"[PIPELINE] Analysis complete for WI#{workItem.Id}");
    }

    // ── KROK 1 ──────────────────────────────────────────────────────────────
    private async Task<ResearchFindings> RunResearcherAsync(WorkItemEvent workItem)
    {
        Console.WriteLine($"[RESEARCHER] Searching DevOps items and WIKI...");

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

        var result = await _researcher.RunAsync(prompt);

        // Parsujemy strukturalny output Researchera
        return JsonSerializer.Deserialize<ResearchFindings>(result)!;
    }

    // ── KROK 2 + 3 ──────────────────────────────────────────────────────────
    private async Task<string> RunWriterEditorLoopAsync(
        WorkItemEvent workItem,
        ResearchFindings findings)
    {
        var retries = 0;
        string report;
        EditorDecision decision;

        do
        {
            // WRITER: produkuje raport
            Console.WriteLine($"[WRITER] Generating report (attempt {retries + 1})...");
            report = await RunWriterAsync(workItem, findings,
                previousFeedback: retries > 0 ? decision!.Feedback : null);

            // EDITOR: ocenia raport
            Console.WriteLine($"[EDITOR] Reviewing report...");
            decision = await RunEditorAsync(workItem, findings, report);

            if (!decision.IsApproved)
                Console.WriteLine($"[EDITOR] Rejected. Feedback: {decision.Feedback}");

            retries++;

        } while (!decision.IsApproved && retries < MaxEditorRetries);

        if (!decision.IsApproved)
        {
            // Po max retry — użyj ostatniego raportu z adnotacją
            Console.WriteLine($"[EDITOR] Max retries reached, using last draft.");
            report = $"> ⚠️ Note: Report may be incomplete.\n\n{report}";
        }

        return report;
    }

    private async Task<string> RunWriterAsync(
        WorkItemEvent workItem,
        ResearchFindings findings,
        string? previousFeedback)
    {
        var feedbackSection = previousFeedback != null
            ? $"\n\n## Editor Feedback to Address:\n{previousFeedback}"
            : string.Empty;

        var prompt = $"""
            Write an impact analysis report for:
            Title: {workItem.Title} (#{workItem.Id})
            Type: {workItem.Type}

            Research findings:
            {JsonSerializer.Serialize(findings, JsonOptions.Indented)}
            {feedbackSection}

            Produce a complete markdown report.
        """;

        return await _writer.RunAsync(prompt);
    }

    private async Task<EditorDecision> RunEditorAsync(
        WorkItemEvent workItem,
        ResearchFindings findings,
        string draftReport)
    {
        var prompt = $$"""
            Review this impact analysis report for WI#{{workItem.Id}}.

            Original work item:
            Title: {{workItem.Title}}
            Description: {{workItem.Description}}

            Research findings available:
            {{JsonSerializer.Serialize(findings, JsonOptions.Indented)}}

            Draft report to review:
            {{draftReport}}

            Return JSON: { "isApproved": bool, "feedback": "string or null" }
        """;

        var result = await _editor.RunAsync(prompt);
        return JsonSerializer.Deserialize<EditorDecision>(result)!;
    }

    // ── KROK 4 ──────────────────────────────────────────────────────────────
    private async Task RunSenderAsync(int workItemId, string report)
    {
        Console.WriteLine($"[SENDER] Posting report to WI#{workItemId}...");

        var prompt = $"""
            Post this approved impact analysis report as a comment 
            on work item #{workItemId}:

            {report}
        """;

        await _sender.RunAsync(prompt);
    }
}