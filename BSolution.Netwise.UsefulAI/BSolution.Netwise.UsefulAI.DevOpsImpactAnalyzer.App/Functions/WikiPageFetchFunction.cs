using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 2/4 — pobiera treść pojedynczej strony WIKI z DevOps API, zapisuje
/// <see cref="WikiPageContentMessage"/> jako blob i publikuje
/// <see cref="BlobRefMessage"/> na kolejce <c>wiki-pages</c> (Claim-Check Pattern).
///
/// Zwraca <c>null</c> dla stron bez treści (katalogi) i przy błędzie pobierania
/// — zapobiega retry storm i dead-lettering na zepsutych stronach.
/// Throttling DevOps API kontroluje
/// <c>host.json → extensions.serviceBus.maxConcurrentCalls</c>.
/// </summary>
public class WikiPageFetchFunction(
    IAzureDevOpsService devOps,
    IBlobMessageStore blobStore,
    ILogger<WikiPageFetchFunction> logger)
{
    [Function(nameof(WikiPageFetchFunction))]
    [ServiceBusOutput("wiki-pages", Connection = "ServiceBus")]
    public async Task<BlobRefMessage?> Run(
        [ServiceBusTrigger("wiki-page-refs", Connection = "ServiceBus")] WikiPageRefMessage message,
        CancellationToken ct)
    {
        logger.LogInformation("[WIKI-FETCH] Fetching '{Path}' from wiki '{Wiki}'...",
            message.Path, message.WikiName ?? message.WikiId);

        try
        {
            var page = await devOps.GetWikiPageAsync(message.WikiId, message.Path, ct);

            if (string.IsNullOrWhiteSpace(page.Content))
            {
                logger.LogInformation(
                    "[WIKI-FETCH] Page '{Path}' has no content (likely a folder) — skipping.",
                    message.Path);
                return null;
            }

            var payload = new WikiPageContentMessage
            {
                WikiId = message.WikiId,
                WikiName = message.WikiName,
                Page = page
            };

            var blobPath = BlobPaths.WikiPage(message.WikiId, message.Path);
            var uri = await blobStore.UploadAsync(blobPath, payload, ct);

            return new BlobRefMessage { BlobUri = uri };
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[WIKI-FETCH] Failed to fetch page '{Path}' from wiki '{Wiki}' — dropping.",
                message.Path, message.WikiName ?? message.WikiId);

            // Świadomie zwracamy null — pojedyncza popsuta strona nie powinna
            // blokować całej synchronizacji przez retry/dead-letter.
            return null;
        }
    }
}
