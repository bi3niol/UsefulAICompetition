using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 2/4 — pobiera treść pojedynczej strony WIKI z DevOps API i publikuje
/// <see cref="WikiPageContentMessage"/> na kolejce <c>wiki-pages</c>.
///
/// W odróżnieniu od work itemów, DevOps WIKI API nie ma batchowego endpointu na
/// strony, więc nie paczkujemy ID — każda strona to osobna wiadomość.
/// Throttling wywołań DevOps API kontroluje
/// <c>host.json → extensions.serviceBus.maxConcurrentCalls</c>.
/// </summary>
public class WikiPageFetchFunction(
    IAzureDevOpsService devOps,
    ILogger<WikiPageFetchFunction> logger)
{
    [Function(nameof(WikiPageFetchFunction))]
    [ServiceBusOutput("wiki-pages", Connection = "ServiceBus")]
    public async Task<WikiPageContentMessage?> Run(
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

            return new WikiPageContentMessage
            {
                WikiId = message.WikiId,
                WikiName = message.WikiName,
                Page = page
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[WIKI-FETCH] Failed to fetch page '{Path}' from wiki '{Wiki}' — dropping.",
                message.Path, message.WikiName ?? message.WikiId);

            // Świadomie zwracamy null zamiast rzucać — pojedyncza popsuta strona nie
            // powinna wracać przez retry/dead-letter i blokować całej synchronizacji.
            return null;
        }
    }
}
