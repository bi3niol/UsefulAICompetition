using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 2/4 — pobiera szczegóły wszystkich work itemów z paczki + komentarze,
/// zapisuje każdy <see cref="WorkItemDetailMessage"/> jako blob w Blob Storage
/// i publikuje cienką wiadomość <see cref="BlobRefMessage"/> na kolejce
/// <c>workitem-details</c> (Claim-Check Pattern).
/// </summary>
public class WorkItemFetchFunction(
    IAzureDevOpsService devOps,
    IBlobMessageStore blobStore,
    ILogger<WorkItemFetchFunction> logger)
{
    /// <summary>Maksymalna liczba równoczesnych wywołań endpointu komentarzy (limit DevOps API).</summary>
    private const int MaxParallelCommentRequests = 4;

    [Function(nameof(WorkItemFetchFunction))]
    [ServiceBusOutput("workitem-details", Connection = "ServiceBus")]
    public async Task<BlobRefMessage[]> Run(
        [ServiceBusTrigger("workitem-ids", Connection = "ServiceBus")] WorkItemIdsBatchMessage message,
        CancellationToken ct)
    {
        if (message.Ids.Count == 0) return [];

        logger.LogInformation("[WI-FETCH] Fetching {Count} work item(s)...", message.Ids.Count);

        // Krok 1: batch fetch szczegółów (jedno żądanie HTTP per 200 ID)
        var workItems = await devOps.GetWorkItemsBatchAsync(message.Ids, ct);

        // Krok 2: dociągnięcie komentarzy (osobny endpoint per WI) z throttlingiem
        var commentSemaphore = new SemaphoreSlim(MaxParallelCommentRequests, MaxParallelCommentRequests);

        await Task.WhenAll(workItems.Select(async wi =>
        {
            await commentSemaphore.WaitAsync(ct);
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
                commentSemaphore.Release();
            }
        }));

        // Krok 3: upload każdego WI do Blob Storage, na SB wysyłamy tylko URI (Claim-Check)
        var refs = await Task.WhenAll(workItems.Select(async wi =>
        {
            var blobPath = BlobPaths.WorkItemDetail(wi.Id);
            var uri = await blobStore.UploadAsync(blobPath, new WorkItemDetailMessage { WorkItem = wi }, ct);
            return new BlobRefMessage { BlobUri = uri };
        }));

        logger.LogInformation(
            "[WI-FETCH] Uploaded {Count} blob(s), enqueued on 'workitem-details'.",
            refs.Length);

        return refs;
    }
}
