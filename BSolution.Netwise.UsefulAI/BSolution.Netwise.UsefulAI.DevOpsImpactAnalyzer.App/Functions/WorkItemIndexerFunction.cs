using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

public class WorkItemIndexerFunction(
    IWorkItemIndexer indexer,
    ILogger<WorkItemIndexerFunction> logger)
{
    /// <summary>
    /// Timer trigger co 15 minut.
    /// Pierwsze uruchomienie (brak historii) → pełna synchronizacja.
    /// Kolejne → synchronizacja przyrostowa od ostatniego uruchomienia.
    /// </summary>
    //[Function(nameof(WorkItemIndexerFunction))]
    public async Task Run(
        [TimerTrigger("0 */15 * * * *")] TimerInfo timerInfo,
        CancellationToken ct)
    {
        if (timerInfo.IsPastDue)
            logger.LogWarning("[INDEXER-FUNC] Timer is running late — previous execution was delayed.");

        // ScheduleStatus?.Last jest null przy pierwszym uruchomieniu
        var lastRun = timerInfo.ScheduleStatus?.Last;
        var isFirstRun = lastRun is null || lastRun == default(DateTime);

        try
        {
            if (isFirstRun)
            {
                logger.LogInformation("[INDEXER-FUNC] First run detected — starting full sync...");
                await indexer.RunFullSyncAsync(ct);
            }
            else
            {
                logger.LogInformation(
                    "[INDEXER-FUNC] Incremental sync since {Since:O}...", lastRun);
                await indexer.RunIncrementalSyncAsync(lastRun!.Value, ct);
            }

            logger.LogInformation("[INDEXER-FUNC] Sync completed successfully.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("[INDEXER-FUNC] Sync was cancelled (function timeout or shutdown).");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[INDEXER-FUNC] Sync failed.");
            throw;
        }
    }
}