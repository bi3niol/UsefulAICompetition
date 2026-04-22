using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 4/4 — wgrywa gotowe dokumenty (MergeOrUpload) do indeksu Azure AI Search.
/// </summary>
public class WorkItemUploadFunction(
    IWorkItemSearchUploader uploader,
    ILogger<WorkItemUploadFunction> logger)
{
    [Function(nameof(WorkItemUploadFunction))]
    public async Task Run(
        [ServiceBusTrigger("workitem-documents", Connection = "ServiceBus")] WorkItemIndexDocumentsMessage message,
        CancellationToken ct)
    {
        if (message.Documents.Count == 0)
        {
            logger.LogInformation("[WI-UPLOAD] Empty document message — skipping.");
            return;
        }

        logger.LogInformation(
            "[WI-UPLOAD] Uploading {Count} document(s)...",
            message.Documents.Count);

        await uploader.UploadAsync(message.Documents, ct);
    }
}
