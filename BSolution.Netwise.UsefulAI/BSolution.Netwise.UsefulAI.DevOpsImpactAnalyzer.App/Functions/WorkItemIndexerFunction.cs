using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;
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
    ISettingsStore settings,
    ILogger<WorkItemIndexerFunction> logger)
{
    /// <summary>Liczba ID na pojedynczą wiadomość Service Bus (= rozmiar paczki dla DevOps batch fetch).</summary>
    private const int IdsPerBatch = 100;

    //[Function(nameof(WorkItemIndexerFunction))]
    [ServiceBusOutput("workitem-ids", Connection = "ServiceBus")]
    public async Task<WorkItemIdsBatchMessage[]> Run(
        [TimerTrigger("0 0 0 * * *", RunOnStartup = true)] TimerInfo timerInfo,
        CancellationToken ct)
    {
        // Stan odczytany z tabeli konfiguracyjnej — null oznacza pierwsze uruchomienie.
        var lastRun = await settings.GetAsync<DateTimeOffset?>(SettingKeys.WorkItemsLastSync, ct);
        var isFirstRun = lastRun is null;

        // Snapshot momentu STARTU — zapisujemy go po sukcesie. Dzięki temu kolejny
        // przebieg widzi jako "od kiedy" punkt sprzed query, więc nie zgubi WI
        // zmienionych w trakcie aktualnego biegu.
        var runStartedUtc = DateTimeOffset.UtcNow;

        DateTime? since = isFirstRun ? null : lastRun!.Value.UtcDateTime;
        logger.LogInformation("[INDEXER-FUNC] Querying work item ids ({Mode})...",
            isFirstRun ? "FULL" : $"INCREMENTAL since {lastRun:O}");

        var ids = await queryService.QueryIdsAsync(since, ct);

        var batches = ids
            .Chunk(IdsPerBatch)
            .Select(batch => new WorkItemIdsBatchMessage { Ids = batch.ToList() })
            .ToArray();

        logger.LogInformation("[INDEXER-FUNC] Enqueued {Batches} batch(es) ({TotalIds} id(s)) on 'workitem-ids'.",
            batches.Length, ids.Count);
        
        await settings.UpsertAsync(SettingKeys.WorkItemsLastSync, runStartedUtc, ct);

        return batches;
    }
}