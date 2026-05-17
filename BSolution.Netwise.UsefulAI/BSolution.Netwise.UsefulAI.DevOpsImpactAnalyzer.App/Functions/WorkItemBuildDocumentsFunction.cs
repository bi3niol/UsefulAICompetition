using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using BSolution.Netwise.UsefulAI.Core.Stores;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 3/4 — pobiera <see cref="WorkItemDetailMessage"/> z bloba, buduje wszystkie
/// chunki z embeddingami i zapisuje całą listę <see cref="WorkItemIndexDocument"/>
/// jako jeden blob w Blob Storage. Na kolejkę <c>workitem-documents</c> trafia
/// jedna <see cref="BlobRefMessage"/> per work item (Claim-Check Pattern).
///
/// Jeden blob na WI eliminuje limit 256 KB SB — blob może mieć dowolny rozmiar.
///
/// Limity wywołań Embedding API kontroluje
/// <c>host.json → extensions.serviceBus.maxConcurrentCalls</c>.
/// </summary>
public class WorkItemBuildDocumentsFunction(
    IWorkItemDocumentBuilder builder,
    IBlobMessageStore blobStore,
    ILogger<WorkItemBuildDocumentsFunction> logger)
{
    [Function(nameof(WorkItemBuildDocumentsFunction))]
    [ServiceBusOutput("workitem-documents", Connection = "ServiceBus")]
    public async Task<BlobRefMessage?> Run(
        [ServiceBusTrigger("workitem-details", Connection = "ServiceBus")] BlobRefMessage message,
        CancellationToken ct)
    {
        var detail = await blobStore.DownloadAsync<WorkItemDetailMessage>(message.BlobUri, ct);
        var wi = detail.WorkItem;

        logger.LogInformation("[WI-BUILD] Building documents for WI#{Id}...", wi.Id);

        var documents = await builder.BuildAsync(wi, ct);

        if (documents.Count == 0)
        {
            logger.LogInformation("[WI-BUILD] WI#{Id} produced no documents.", wi.Id);
            return null;
        }

        // Wszystkie chunki jednego WI w jednym blobie — brak limitu rozmiaru, jedna wiadomość SB.
        var blobPath = BlobPaths.WorkItemDocument(wi.Id);
        var uri = await blobStore.UploadAsync(blobPath, documents, ct);

        logger.LogInformation("[WI-BUILD] WI#{Id} → {Count} chunk(s) uploaded to single blob.", wi.Id, documents.Count);
        return new BlobRefMessage { BlobUri = uri };
    }
}
