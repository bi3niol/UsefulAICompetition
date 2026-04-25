using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 4/4 — pobiera z bloba pełną listę chunków <see cref="WikiIndexDocument"/>
/// dla jednej strony WIKI i wgrywa je do indeksu Azure AI Search
/// <c>wiki-pages-index</c> (upsert, partie ≤ 500).
/// </summary>
public class WikiUploadFunction(
    IWikiSearchUploader uploader,
    IBlobMessageStore blobStore,
    ILogger<WikiUploadFunction> logger)
{
    [Function(nameof(WikiUploadFunction))]
    public async Task Run(
        [ServiceBusTrigger("wiki-documents", Connection = "ServiceBus")] BlobRefMessage message,
        CancellationToken ct)
    {
        var documents = await blobStore.DownloadAsync<List<WikiIndexDocument>>(message.BlobUri, ct);

        if (documents.Count == 0)
        {
            logger.LogInformation("[WIKI-UPLOAD] Blob contained no documents — skipping.");
            return;
        }

        await uploader.UploadAsync(documents, ct);

        logger.LogInformation("[WIKI-UPLOAD] Uploaded {Count} document(s) to AI Search.", documents.Count);
    }
}
