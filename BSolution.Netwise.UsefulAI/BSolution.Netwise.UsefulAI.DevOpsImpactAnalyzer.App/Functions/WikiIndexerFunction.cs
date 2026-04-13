using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

public class WikiIndexerFunction(
    IWikiIndexer indexer,
    ILogger<WikiIndexerFunction> logger)
{
    /// <summary>
    /// Pełna synchronizacja WIKI co godzinę.
    /// WIKI zmienia się rzadziej niż work itemy — brak etapu incremental.
    /// </summary>
    [Function(nameof(WikiIndexerFunction))]
    public async Task Run(
        [TimerTrigger("0 0 * * * *")] TimerInfo timerInfo,
        CancellationToken ct)
    {
        if (timerInfo.IsPastDue)
            logger.LogWarning("[WIKI-INDEXER-FUNC] Timer is running late.");

        logger.LogInformation("[WIKI-INDEXER-FUNC] Starting wiki sync...");

        try
        {
            await indexer.RunSyncAsync(ct);
            logger.LogInformation("[WIKI-INDEXER-FUNC] Sync completed successfully.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("[WIKI-INDEXER-FUNC] Sync cancelled (timeout or shutdown).");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[WIKI-INDEXER-FUNC] Sync failed.");
            throw;
        }
    }
}