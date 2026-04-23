using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 4/4 — wgrywa gotowe dokumenty (MergeOrUpload) do indeksu Azure AI Search
/// <c>wiki-pages-index</c>.
/// </summary>
public class WikiUploadFunction(
    IWikiSearchUploader uploader,
    ILogger<WikiUploadFunction> logger)
{
    [Function(nameof(WikiUploadFunction))]
    public async Task Run(
        [ServiceBusTrigger("wiki-documents", Connection = "ServiceBus")] WikiIndexDocumentsMessage message,
        CancellationToken ct)
    {
        if (message.Documents.Count == 0)
        {
            logger.LogInformation("[WIKI-UPLOAD] Empty document message — skipping.");
            return;
        }

        logger.LogInformation(
            "[WIKI-UPLOAD] Uploading {Count} document(s)...",
            message.Documents.Count);

        await uploader.UploadAsync(message.Documents, ct);
    }
}
