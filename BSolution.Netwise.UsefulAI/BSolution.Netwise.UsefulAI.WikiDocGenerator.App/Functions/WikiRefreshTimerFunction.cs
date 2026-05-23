using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.Core.Services;
using BSolution.Netwise.UsefulAI.Core.Stores;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Messages;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Functions;

/// <summary>
/// Stage 1 — Cykliczny trigger (raz na dobę o 02:00 UTC).
/// Pobiera ID work itemów zmienionych od ostatniego biegu i ENQUEUE'uje je
/// w paczkach po <see cref="WorkItemsPerBatch"/> na kolejkę <c>wikigen-pipeline</c>.
/// Ciężkie przetwarzanie (pipeline LLM) odbywa się w <see cref="WikiDocPipelineFunction"/>.
///
/// Wzorzec identyczny jak <c>WorkItemIndexerFunction</c> w Impact Analyzerze:
/// timer/webhook to Stage 1 (szybka, lekka), pipeline to Stage 2 (timeout-safe).
/// </summary>
public class WikiRefreshTimerFunction(
    IWorkItemQueryService queryService,
    ISettingsStore settings,
    ILogger<WikiRefreshTimerFunction> logger)
{
    private const int WorkItemsPerBatch = 20;

    [Function(nameof(WikiRefreshTimerFunction))]
    [ServiceBusOutput("wikigen-pipeline", Connection = "ServiceBus")]
    public async Task<WikiGenPipelineMessage[]> Run(
        [TimerTrigger("0 0 2 * * *", RunOnStartup = false)] TimerInfo timer,
        CancellationToken ct)
    {
        var lastRun = await settings.GetAsync<DateTimeOffset?>(SettingKeys.WikiGenLastSync, ct);
        var isFirstRun = lastRun is null;
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
            return [];
        }

        var messages = ids
            .Chunk(WorkItemsPerBatch)
            .Select(batch => new WikiGenPipelineMessage
            {
                Source = WikiGenSource.WorkItems,
                WorkItemIds = batch.ToList()
            })
            .ToArray();

        logger.LogInformation(
            "[WIKIGEN-TIMER] Enqueued {Batches} batch(es) ({Total} work item(s)) on 'wikigen-pipeline'.",
            messages.Length, ids.Count);

        await settings.UpsertAsync(SettingKeys.WikiGenLastSync, runStartedUtc, ct);
        return messages;
    }
}
