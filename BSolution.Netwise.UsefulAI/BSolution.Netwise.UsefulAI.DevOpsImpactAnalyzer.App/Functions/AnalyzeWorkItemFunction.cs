using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

public class AnalyzeWorkItemFunction
{
    private readonly ILogger<AnalyzeWorkItemFunction> _logger;
    private readonly IAzureDevOpsService _devOps;
    private readonly ImpactAnalysisPipeline _pipeline;

    public AnalyzeWorkItemFunction(
        ILogger<AnalyzeWorkItemFunction> logger,
        IAzureDevOpsService devOps,
        ImpactAnalysisPipeline pipeline)
    {
        _logger = logger;
        _devOps = devOps;
        _pipeline = pipeline;
    }

    /// <summary>
    /// HTTP trigger — pobiera work item po ID z Azure DevOps i uruchamia
    /// pipeline analizy konfliktów. Zwraca tekstowy raport (markdown).
    ///
    /// Użycie:
    ///   GET  /api/AnalyzeWorkItem/{workItemId}
    ///   GET  /api/AnalyzeWorkItem?id={workItemId}
    /// </summary>
    [Function(nameof(AnalyzeWorkItemFunction))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "AnalyzeWorkItem/{workItemId:int?}")]
        HttpRequest req,
        int? workItemId,
        CancellationToken ct)
    {
        var id = workItemId;
        if (id is null && int.TryParse(req.Query["id"], out var queryId))
            id = queryId;

        if (id is null || id <= 0)
            return new BadRequestObjectResult("Missing or invalid work item id. Use route '/api/AnalyzeWorkItem/{id}' or query '?id={id}'.");

        _logger.LogInformation("[ANALYZE-FUNC] Fetching WI#{WorkItemId} from Azure DevOps...", id);

        Models.WorkItemDetail detail;
        try
        {
            detail = await _devOps.GetWorkItemAsync(id.Value, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[ANALYZE-FUNC] Failed to fetch WI#{WorkItemId}.", id);
            return new ObjectResult($"Failed to fetch work item #{id}: {ex.Message}")
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
            "[ANALYZE-FUNC] Running impact analysis pipeline for WI#{WorkItemId} ({Type}: {Title})...",
            workItemEvent.Id, workItemEvent.Type, workItemEvent.Title);

        try
        {
            var report = await _pipeline.RunAsync(workItemEvent);
            return new ContentResult
            {
                Content = report,
                ContentType = "text/markdown; charset=utf-8",
                StatusCode = StatusCodes.Status200OK
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ANALYZE-FUNC] Pipeline failed for WI#{WorkItemId}.", id);
            return new ObjectResult($"Pipeline execution failed: {ex.Message}")
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
