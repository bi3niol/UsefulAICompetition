using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 1/4 — Timer trigger.
/// Wykonuje wyłącznie enumerację wszystkich stron WIKI (lista wiki + płaska lista
/// ścieżek per wiki) i publikuje pojedyncze wiadomości <see cref="WikiPageRefMessage"/>
/// na kolejce <c>wiki-page-refs</c>. Reszta przetwarzania (pobranie treści, embedding,
/// upload) odbywa się asynchronicznie przez kolejne funkcje konsumujące Service Bus.
/// </summary>
public class WikiIndexerFunction(
    IWikiPageQueryService queryService,
    ILogger<WikiIndexerFunction> logger)
{
    [Function(nameof(WikiIndexerFunction))]
    [ServiceBusOutput("wiki-page-refs", Connection = "ServiceBus")]
    public async Task<WikiPageRefMessage[]> Run(
        [TimerTrigger("0 0 0 * * *", RunOnStartup = true)] TimerInfo timerInfo,
        CancellationToken ct)
    {
        if (timerInfo.IsPastDue)
            logger.LogWarning("[WIKI-INDEXER-FUNC] Timer is running late.");

        logger.LogInformation("[WIKI-INDEXER-FUNC] Enumerating WIKI pages...");

        var refs = await queryService.QueryAllPageRefsAsync(ct);

        if (refs.Count == 0)
        {
            logger.LogInformation("[WIKI-INDEXER-FUNC] No WIKI pages found — nothing enqueued.");
            return [];
        }

        logger.LogInformation(
            "[WIKI-INDEXER-FUNC] Enqueued {Count} page ref(s) on 'wiki-page-refs'.",
            refs.Count);

        return [.. refs];
    }
}
