using BSolution.Netwise.UsefulAI.Core.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Sender;


public class PostCommentTool(IAzureDevOpsService devOpsService)
{
    // Header który bêdzie nag³ówkiem ka¿dego komentarza AI
    private const string CommentHeader = """
        > ?? **Automated Impact Analysis** | Powered by AI Agent
        > *This report was generated automatically. Please review before acting.*
        
        ---
        
        """;

    [AgentTool(Description = """
        Posts the final approved impact analysis report as a comment 
        on the specified Azure DevOps work item.
        Call this EXACTLY ONCE with the complete, approved report.
        Do NOT call this multiple times or with partial reports.
        Do NOT modify or shorten the report content.
        """)]
    public async Task<string> PostCommentToWorkItemAsync(
        [Description("The numeric ID of the work item to comment on")]
        int workItemId,

        [Description("The complete approved markdown impact analysis report")]
        string reportContent)
    {
        // Walidacja — nie dopuszczamy pustego raportu
        if (string.IsNullOrWhiteSpace(reportContent))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Report content cannot be empty. Provide the full markdown report."
            });
        }

        // Dodajemy header do raportu
        var fullComment = CommentHeader + reportContent;

        var result = await devOpsService.AddCommentAsync(workItemId, fullComment);

        return JsonSerializer.Serialize(new
        {
            success = true,
            message = result,
            workItemId,
            reportLength = reportContent.Length
        });
    }
}