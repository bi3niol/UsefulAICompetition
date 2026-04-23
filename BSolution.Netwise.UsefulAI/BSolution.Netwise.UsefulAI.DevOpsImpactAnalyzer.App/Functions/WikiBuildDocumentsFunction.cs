using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 3/4 — buduje listę chunkowanych dokumentów Azure Search (z embeddingami)
/// dla pojedynczej strony WIKI i publikuje wynik na kolejce <c>wiki-documents</c>.
///
/// Limity wywołań Embedding API kontroluje
/// <c>host.json → extensions.serviceBus.maxConcurrentCalls</c>.
/// </summary>
public class WikiBuildDocumentsFunction(
    IWikiDocumentBuilder builder,
    ILogger<WikiBuildDocumentsFunction> logger)
{
    [Function(nameof(WikiBuildDocumentsFunction))]
    [ServiceBusOutput("wiki-documents", Connection = "ServiceBus")]
    public async Task<WikiIndexDocumentsMessage?> Run(
        [ServiceBusTrigger("wiki-pages", Connection = "ServiceBus")] WikiPageContentMessage message,
        CancellationToken ct)
    {
        logger.LogInformation("[WIKI-BUILD] Building documents for '{Path}'...",
            message.Page.Path);

        var documents = await builder.BuildAsync(message.WikiId, message.Page, ct);

        if (documents.Count == 0)
        {
            logger.LogInformation("[WIKI-BUILD] Page '{Path}' produced no documents.",
                message.Page.Path);
            return null;
        }

        return new WikiIndexDocumentsMessage { Documents = documents };
    }
}
