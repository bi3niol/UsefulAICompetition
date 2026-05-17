using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using BSolution.Netwise.UsefulAI.Core.Stores;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 3/4 — pobiera <see cref="WikiPageContentMessage"/> z bloba, buduje wszystkie
/// chunki Markdown z embeddingami i zapisuje całą listę <see cref="WikiIndexDocument"/>
/// jako jeden blob w Blob Storage. Na kolejkę <c>wiki-documents</c> trafia
/// jedna <see cref="BlobRefMessage"/> per strona WIKI (Claim-Check Pattern).
///
/// Jeden blob na stronę eliminuje limit 256 KB SB — blob może mieć dowolny rozmiar.
///
/// Limity wywołań Embedding API kontroluje
/// <c>host.json → extensions.serviceBus.maxConcurrentCalls</c>.
/// </summary>
public class WikiBuildDocumentsFunction(
    IWikiDocumentBuilder builder,
    IBlobMessageStore blobStore,
    ILogger<WikiBuildDocumentsFunction> logger)
{
    [Function(nameof(WikiBuildDocumentsFunction))]
    [ServiceBusOutput("wiki-documents", Connection = "ServiceBus")]
    public async Task<BlobRefMessage?> Run(
        [ServiceBusTrigger("wiki-pages", Connection = "ServiceBus")] BlobRefMessage message,
        CancellationToken ct)
    {
        var content = await blobStore.DownloadAsync<WikiPageContentMessage>(message.BlobUri, ct);

        logger.LogInformation("[WIKI-BUILD] Building documents for '{Path}'...", content.Page.Path);

        var documents = await builder.BuildAsync(content.WikiId, content.Page, ct);

        if (documents.Count == 0)
        {
            logger.LogInformation("[WIKI-BUILD] Page '{Path}' produced no documents.", content.Page.Path);
            return null;
        }

        // Wszystkie chunki jednej strony w jednym blobie — brak limitu rozmiaru, jedna wiadomość SB.
        var blobPath = BlobPaths.WikiDocument(content.WikiId, content.Page.Path ?? content.WikiId);
        var uri = await blobStore.UploadAsync(blobPath, documents, ct);

        logger.LogInformation("[WIKI-BUILD] Page '{Path}' → {Count} chunk(s) uploaded to single blob.",
            content.Page.Path, documents.Count);

        return new BlobRefMessage { BlobUri = uri };
    }
}
