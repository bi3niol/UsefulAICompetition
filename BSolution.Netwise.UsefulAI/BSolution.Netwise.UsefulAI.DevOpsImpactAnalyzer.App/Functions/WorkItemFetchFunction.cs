using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 2/4 — pobiera szczegóły wszystkich work itemów z paczki + komentarze
/// (osobny endpoint w DevOps API) i publikuje pojedyncze wiadomości
/// <see cref="WorkItemDetailMessage"/> na kolejce <c>workitem-details</c>.
/// </summary>
public class WorkItemFetchFunction(
    IAzureDevOpsService devOps,
    ILogger<WorkItemFetchFunction> logger)
{
    /// <summary>Maksymalna liczba równoczesnych wywołań endpointu komentarzy (limit DevOps API).</summary>
    private const int MaxParallelCommentRequests = 4;

    [Function(nameof(WorkItemFetchFunction))]
    [ServiceBusOutput("workitem-details", Connection = "ServiceBus")]
    public async Task<WorkItemDetailMessage[]> Run(
        [ServiceBusTrigger("workitem-ids", Connection = "ServiceBus")] WorkItemIdsBatchMessage message,
        CancellationToken ct)
    {
        if (message.Ids.Count == 0) return [];

        logger.LogInformation("[WI-FETCH] Fetching {Count} work item(s)...", message.Ids.Count);

        // Krok 1: batch fetch szczegółów (jedno żądanie HTTP per 200 ID)
        var workItems = await devOps.GetWorkItemsBatchAsync(message.Ids, ct);

        // Krok 2: dociągnięcie komentarzy (osobny endpoint per WI) z throttlingiem
        var semaphore = new SemaphoreSlim(MaxParallelCommentRequests, MaxParallelCommentRequests);

        await Task.WhenAll(workItems.Select(async wi =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                wi.Comments = await devOps.GetWorkItemCommentsAsync(wi.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[WI-FETCH] Failed to fetch comments for WI#{Id} — continuing without them.",
                    wi.Id);
                wi.Comments = [];
            }
            finally
            {
                semaphore.Release();
            }
        }));

        logger.LogInformation(
            "[WI-FETCH] Enqueued {Count} work item(s) on 'workitem-details'.",
            workItems.Count);

        return workItems
            .Select(wi => new WorkItemDetailMessage { WorkItem = wi })
            .ToArray();
    }
}
