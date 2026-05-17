using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.Core.Services;
using BSolution.Netwise.UsefulAI.Core.Stores;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Functions;

/// <summary>
/// Cykliczny trigger (raz na dobę o 02:00 UTC) — pobiera Feature / User Story /
/// PBI / Epic zmienione od ostatniego biegu i odpala pipeline aktualizacji wiki.
///
/// Stan inkrementalny trzymany w tabeli <c>Settings</c> pod kluczem
/// <see cref="SettingKeys.WikiGenLastSync"/>. Pierwsze uruchomienie robi pełny
/// skan (since = null) — kolejne tylko delty.
///
/// Wzorzec identyczny jak <c>WorkItemIndexerFunction</c> w Impact Analyzerze,
/// ale BEZ Service Bus claim-check: pipeline wiki jest synchroniczny w obrębie
/// funkcji. Jeśli wolumeny urosną, dodamy etap Service Bus identycznie jak tam.
/// </summary>
public class WikiRefreshTimerFunction(
    IWorkItemQueryService queryService,
    WikiDocGenerationPipeline pipeline,
    ISettingsStore settings,
    ILogger<WikiRefreshTimerFunction> logger)
{
    /// <summary>
    /// Rozmiar paczki work itemów przekazywanej do pojedynczego biegu pipeline'u.
    /// Researcher musi przeczytać każdy WI, więc trzymamy to nisko, żeby kontekst
    /// LLM był sensowny i czas pojedynczego biegu mieścił się w timeout funkcji.
    /// </summary>
    private const int WorkItemsPerPipelineRun = 20;

    [Function(nameof(WikiRefreshTimerFunction))]
    public async Task Run(
        [TimerTrigger("0 0 2 * * *", RunOnStartup = false)] TimerInfo timer,
        CancellationToken ct)
    {
        var lastRun = await settings.GetAsync<DateTimeOffset?>(SettingKeys.WikiGenLastSync, ct);
        var isFirstRun = lastRun is null;

        // Snapshot momentu STARTU — zapisujemy po sukcesie, żeby nie zgubić
        // work itemów zmienionych w trakcie biegu.
        var runStartedUtc = DateTimeOffset.UtcNow;
        DateTime? since = isFirstRun ? null : lastRun!.Value.UtcDateTime;

        logger.LogInformation(
            "[WIKIGEN-TIMER] Starting wiki refresh ({Mode}{Since}).",
            isFirstRun ? "FULL" : "INCREMENTAL",
            isFirstRun ? string.Empty : $" since {lastRun:O}");

        var ids = await queryService.QueryIdsAsync(since, ct);
        if (ids.Count == 0)
        {
            logger.LogInformation("[WIKIGEN-TIMER] Nothing to do — no changed work items.");
            await settings.UpsertAsync(SettingKeys.WikiGenLastSync, runStartedUtc, ct);
            return;
        }

        var batches = ids.Chunk(WorkItemsPerPipelineRun).ToList();
        logger.LogInformation(
            "[WIKIGEN-TIMER] Processing {Total} work item(s) in {Batches} batch(es) of {Size}.",
            ids.Count, batches.Count, WorkItemsPerPipelineRun);

        var failures = 0;
        foreach (var (batch, idx) in batches.Select((b, i) => (b, i + 1)))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                logger.LogInformation(
                    "[WIKIGEN-TIMER] Batch {Index}/{Total}: [{Ids}]",
                    idx, batches.Count, string.Join(", ", batch));

                var request = new WorkItemsWikiRefreshRequest(
                    WorkItemIds: batch,
                    RepositoryId: null);

                await pipeline.RunForWorkItemsAsync(request, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Pojedynczy padający batch nie powinien zatrzymać całego biegu —
                // zostawiamy watermark BEZ aktualizacji, więc te WI wpadną
                // następnym razem (idempotentne dzięki ETag-om przy upsert).
                failures++;
                logger.LogError(ex,
                    "[WIKIGEN-TIMER] Batch {Index}/{Total} failed.",
                    idx, batches.Count);
            }
        }

        if (failures == 0)
        {
            await settings.UpsertAsync(SettingKeys.WikiGenLastSync, runStartedUtc, ct);
            logger.LogInformation("[WIKIGEN-TIMER] Done. Watermark advanced to {Watermark:O}.", runStartedUtc);
        }
        else
        {
            logger.LogWarning(
                "[WIKIGEN-TIMER] Completed with {Failures} failed batch(es); watermark NOT advanced.",
                failures);
        }
    }
}
