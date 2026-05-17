using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;
using BSolution.Netwise.UsefulAI.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// HTTP trigger — uruchamia pipeline analizy konfliktów dla podanego work itemu
/// i zapisuje wygenerowany raport (markdown) w blob storage pod
/// <c>reports/{workItemId}.md</c>. Zwraca też raport w odpowiedzi.
///
/// Użycie:
///   POST /api/workitems/{workItemId}/report
/// </summary>
public class GenerateWorkItemReportFunction
{
    private readonly ILogger<GenerateWorkItemReportFunction> _logger;
    private readonly IAzureDevOpsService _devOps;
    private readonly ImpactAnalysisPipeline _pipeline;
    private readonly IReportStore _reportStore;

    public GenerateWorkItemReportFunction(
        ILogger<GenerateWorkItemReportFunction> logger,
        IAzureDevOpsService devOps,
        ImpactAnalysisPipeline pipeline,
        IReportStore reportStore)
    {
        _logger = logger;
        _devOps = devOps;
        _pipeline = pipeline;
        _reportStore = reportStore;
    }

    [Function(nameof(GenerateWorkItemReportFunction))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "workitems/{workItemId:int}/report")]
        HttpRequest req,
        int workItemId,
        CancellationToken ct)
    {
        if (workItemId <= 0)
            return new BadRequestObjectResult("Invalid work item id.");

        _logger.LogInformation("[GENERATE-FUNC] Fetching WI#{WorkItemId} from Azure DevOps...", workItemId);

        WorkItemDetail detail;
        try
        {
            detail = await _devOps.GetWorkItemAsync(workItemId, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[GENERATE-FUNC] Failed to fetch WI#{WorkItemId}.", workItemId);
            return new ObjectResult($"Failed to fetch work item #{workItemId}: {ex.Message}")
            {
                StatusCode = StatusCodes.Status502BadGateway
            };
        }

        var workItemEvent = new WorkItemEvent(
            Id: detail.Id,
            Type: detail.Type ?? string.Empty,
            Title: detail.Title ?? string.Empty,
            Description: detail.Description ?? string.Empty,
            AcceptanceCriteria: detail.AcceptanceCriteria ?? string.Empty,
            AreaPath: detail.AreaPath ?? string.Empty,
            Tags: detail.Tags ?? string.Empty
        );

        _logger.LogInformation(
            "[GENERATE-FUNC] Running impact analysis pipeline for WI#{WorkItemId} ({Type}: {Title})...",
            workItemEvent.Id, workItemEvent.Type, workItemEvent.Title);

        string report;
        try
        {
            report = await _pipeline.RunAsync(workItemEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GENERATE-FUNC] Pipeline failed for WI#{WorkItemId}.", workItemId);
            return new ObjectResult($"Pipeline execution failed: {ex.Message}")
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        await _reportStore.SaveAsync(workItemId, report, ct);

        return new ContentResult
        {
            Content = report,
            ContentType = "text/markdown; charset=utf-8",
            StatusCode = StatusCodes.Status200OK
        };
    }
}
