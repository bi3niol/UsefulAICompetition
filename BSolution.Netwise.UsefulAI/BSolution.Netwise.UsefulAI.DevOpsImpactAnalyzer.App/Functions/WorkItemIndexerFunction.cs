using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

/// <summary>
/// Etap 1/4 — Timer trigger.
/// Wykonuje wyłącznie zapytanie WIQL i publikuje paczki ID work itemów na kolejce
/// <c>workitem-ids</c>. Reszta przetwarzania (pobranie szczegółów, embedding, upload)
/// odbywa się asynchronicznie przez kolejne funkcje konsumujące Service Bus.
/// </summary>
public class WorkItemIndexerFunction(
    IWorkItemQueryService queryService,
    ILogger<WorkItemIndexerFunction> logger)
{
    /// <summary>Liczba ID na pojedynczą wiadomość Service Bus (= rozmiar paczki dla DevOps batch fetch).</summary>
    private const int IdsPerBatch = 100;

    [Function(nameof(WorkItemIndexerFunction))]
    [ServiceBusOutput("workitem-ids", Connection = "ServiceBus")]
    public async Task<WorkItemIdsBatchMessage[]> Run(
        [TimerTrigger("0 0 0 * * *", RunOnStartup = true)] TimerInfo timerInfo,
        CancellationToken ct)
    {
        if (timerInfo.IsPastDue)
            logger.LogWarning("[INDEXER-FUNC] Timer is running late.");

        // ScheduleStatus?.Last jest null przy pierwszym uruchomieniu
        var lastRun = timerInfo.ScheduleStatus?.Last;
        var isFirstRun = lastRun is null || lastRun == default(DateTime);

        DateTime? since = isFirstRun ? null : lastRun;
        logger.LogInformation(
            "[INDEXER-FUNC] Querying work item ids ({Mode})...",
            isFirstRun ? "FULL" : $"INCREMENTAL since {lastRun:O}");

        var ids = await queryService.QueryIdsAsync(since, ct);

        if (ids.Count == 0)
        {
            logger.LogInformation("[INDEXER-FUNC] Nothing to index — no batches enqueued.");
            return [];
        }

        var batches = ids
            .Chunk(IdsPerBatch)
            .Select(batch => new WorkItemIdsBatchMessage { Ids = batch.ToList() })
            .ToArray();

        logger.LogInformation(
            "[INDEXER-FUNC] Enqueued {Batches} batch(es) ({TotalIds} id(s)) on 'workitem-ids'.",
            batches.Length, ids.Count);

        return batches;
    }
}