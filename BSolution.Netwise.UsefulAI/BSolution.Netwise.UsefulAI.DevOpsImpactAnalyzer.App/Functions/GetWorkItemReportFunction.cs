using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// HTTP trigger — zwraca wcześniej wygenerowany raport Impact Analysis z blob storage.
/// Jeśli raportu nie ma, zwraca 404.
///
/// Użycie:
///   GET /api/workitems/{workItemId}/report
/// </summary>
public class GetWorkItemReportFunction
{
    private readonly ILogger<GetWorkItemReportFunction> _logger;
    private readonly IReportStore _reportStore;

    public GetWorkItemReportFunction(
        ILogger<GetWorkItemReportFunction> logger,
        IReportStore reportStore)
    {
        _logger = logger;
        _reportStore = reportStore;
    }

    [Function(nameof(GetWorkItemReportFunction))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "workitems/{workItemId:int}/report")]
        HttpRequest req,
        int workItemId,
        CancellationToken ct)
    {
        if (workItemId <= 0)
            return new BadRequestObjectResult("Invalid work item id.");

        var report = await _reportStore.TryGetAsync(workItemId, ct);
        if (report is null)
        {
            _logger.LogInformation("[GET-FUNC] No stored report for WI#{WorkItemId}.", workItemId);
            return new NotFoundObjectResult(
                $"No report found for work item #{workItemId}. " +
                $"Generate one via POST /api/workitems/{workItemId}/report.");
        }

        return new ContentResult
        {
            Content = report,
            ContentType = "text/markdown; charset=utf-8",
            StatusCode = StatusCodes.Status200OK
        };
    }
}
