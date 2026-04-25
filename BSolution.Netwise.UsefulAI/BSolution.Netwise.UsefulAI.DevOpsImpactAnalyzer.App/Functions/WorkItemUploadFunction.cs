using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 4/4 — pobiera z bloba pełną listę chunków <see cref="WorkItemIndexDocument"/>
/// dla jednego work itemu i wgrywa je do indeksu Azure AI Search (MergeOrUpload, partie ≤ 500).
/// </summary>
public class WorkItemUploadFunction(
    IWorkItemSearchUploader uploader,
    IBlobMessageStore blobStore,
    ILogger<WorkItemUploadFunction> logger)
{
    [Function(nameof(WorkItemUploadFunction))]
    public async Task Run(
        [ServiceBusTrigger("workitem-documents", Connection = "ServiceBus")] BlobRefMessage message,
        CancellationToken ct)
    {
        var documents = await blobStore.DownloadAsync<List<WorkItemIndexDocument>>(message.BlobUri, ct);

        if (documents.Count == 0)
        {
            logger.LogInformation("[WI-UPLOAD] Blob contained no documents — skipping.");
            return;
        }

        await uploader.UploadAsync(documents, ct);

        logger.LogInformation("[WI-UPLOAD] Uploaded {Count} document(s) to AI Search.", documents.Count);
    }
}
