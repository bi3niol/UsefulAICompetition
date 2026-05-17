using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.Core.Stores;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;
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
    ISettingsStore settings,
    ILogger<WikiIndexerFunction> logger)
{
    [Function(nameof(WikiIndexerFunction))]
    [ServiceBusOutput("wiki-page-refs", Connection = "ServiceBus")]
    public async Task<WikiPageRefMessage[]> Run(
        [TimerTrigger("0 0 */4 * * *", RunOnStartup = true)] TimerInfo timerInfo,
        CancellationToken ct)
    {
        var lastRun = await settings.GetAsync<DateTimeOffset?>(SettingKeys.WikiLastSync, ct);
        var runStartedUtc = DateTimeOffset.UtcNow;

        logger.LogInformation("[WIKI-INDEXER-FUNC] Enumerating WIKI pages (last sync: {LastSync}, mode: {Mode})...",
            lastRun is null ? "never" : lastRun.Value.ToString("O"),
            lastRun is null ? "full" : "incremental");

        var refs = await queryService.QueryPageRefsAsync(lastRun, ct);

        logger.LogInformation("[WIKI-INDEXER-FUNC] Enqueued {Count} page ref(s) on 'wiki-page-refs'.", refs.Count);

        await settings.UpsertAsync(SettingKeys.WikiLastSync, runStartedUtc, ct);

        return [.. refs];
    }
}
