using BSolution.Netwise.UsefulAI.Core.Stores;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Messages;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Stores;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Functions;

/// <summary>
/// Stage 2/4 — Researcher. Konsumuje <see cref="WikiGenPipelineMessage"/> z kolejki
/// <c>wikigen-pipeline</c>, buduje seed prompt, uruchamia agenta Researcher i zapisuje
/// wynik (<see cref="WikiResearchFindings"/>) jako blob (Claim-Check). Na wyjściu
/// enqueue'uje <see cref="BlobRefMessage"/> na <c>wikigen-write</c>.
/// </summary>
public class WikiDocResearchFunction(
    WikiDocGenerationPipeline pipeline,
    IBlobMessageStore blobStore,
    ILogger<WikiDocResearchFunction> logger)
{
    [Function(nameof(WikiDocResearchFunction))]
    [ServiceBusOutput("wikigen-write", Connection = "ServiceBus")]
    public async Task<BlobRefMessage?> Run(
        [ServiceBusTrigger("wikigen-pipeline", Connection = "ServiceBus")]
        WikiGenPipelineMessage message,
        CancellationToken ct)
    {
        var seedPrompt = message.Source switch
        {
            WikiGenSource.PullRequest => BuildPrPrompt(message),
            WikiGenSource.WorkItems => BuildWiPrompt(message),
            _ => null
        };

        if (seedPrompt is null)
        {
            logger.LogWarning("[RESEARCH-FUNC] Unsupported source '{Source}' — skipping.", message.Source);
            return null;
        }

        var context = message.Source == WikiGenSource.PullRequest
            ? $"PR-{message.PullRequestId}"
            : $"WI-{string.Join("-", message.WorkItemIds.Take(5))}";

        logger.LogInformation("[RESEARCH-FUNC] Running Researcher for {Context}...", context);

        var findings = await pipeline.RunResearcherAsync(seedPrompt);

        var blobPath = BlobPaths.Findings(context);
        var blobUri = await blobStore.UploadAsync(blobPath, findings, ct);

        logger.LogInformation("[RESEARCH-FUNC] Findings stored at '{BlobPath}'. Enqueuing to wikigen-write.", blobPath);
        return new BlobRefMessage { BlobUri = blobUri };
    }

    private string BuildPrPrompt(WikiGenPipelineMessage msg)
    {
        var pr = new PullRequestEvent(
            RepositoryId: msg.RepositoryId ?? string.Empty,
            RepositoryName: msg.RepositoryName ?? string.Empty,
            PullRequestId: msg.PullRequestId ?? 0,
            Title: msg.PrTitle ?? string.Empty,
            Description: msg.PrDescription,
            SourceBranch: msg.SourceBranch ?? string.Empty,
            TargetBranch: msg.TargetBranch ?? string.Empty,
            MergeCommitId: msg.MergeCommitId,
            Author: msg.Author,
            LinkedWorkItemIds: msg.LinkedWorkItemIds);
        return pipeline.BuildSeedPromptForPullRequest(pr);
    }

    private string BuildWiPrompt(WikiGenPipelineMessage msg)
    {
        var request = new WorkItemsWikiRefreshRequest(
            WorkItemIds: msg.WorkItemIds,
            RepositoryId: msg.RepositoryId);
        return pipeline.BuildSeedPromptForWorkItems(request);
    }
}

/// <summary>
/// Stage 3/4 — Writer + Editor loop. Konsumuje <see cref="BlobRefMessage"/> z kolejki
/// <c>wikigen-write</c>, pobiera findings z bloba, uruchamia Writer/Editor loop
/// i zapisuje gotowy draft (<see cref="WikiDraft"/>) jako blob. Enqueue'uje
/// <see cref="BlobRefMessage"/> na <c>wikigen-send</c>.
/// </summary>
public class WikiDocWriteFunction(
    WikiDocGenerationPipeline pipeline,
    IBlobMessageStore blobStore,
    ILogger<WikiDocWriteFunction> logger)
{
    [Function(nameof(WikiDocWriteFunction))]
    [ServiceBusOutput("wikigen-send", Connection = "ServiceBus")]
    public async Task<BlobRefMessage?> Run(
        [ServiceBusTrigger("wikigen-write", Connection = "ServiceBus")]
        BlobRefMessage message,
        CancellationToken ct)
    {
        logger.LogInformation("[WRITE-FUNC] Downloading findings from '{Uri}'...", message.BlobUri);
        var findings = await blobStore.DownloadAsync<WikiResearchFindings>(message.BlobUri, ct);

        logger.LogInformation("[WRITE-FUNC] Running Writer + Editor loop...");
        var draft = await pipeline.RunWriterEditorLoopAsync(findings);

        if (draft.Edits.Count == 0)
        {
            logger.LogInformation("[WRITE-FUNC] No edits produced — nothing to send.");
            return null;
        }

        var blobPath = BlobPaths.Draft(findings.Scope ?? "draft");
        var blobUri = await blobStore.UploadAsync(blobPath, draft, ct);

        logger.LogInformation(
            "[WRITE-FUNC] Draft with {Count} edit(s) stored at '{BlobPath}'. Enqueuing to wikigen-send.",
            draft.Edits.Count, blobPath);
        return new BlobRefMessage { BlobUri = blobUri };
    }
}

/// <summary>
/// Stage 4/4 — Sender. Konsumuje <see cref="BlobRefMessage"/> z kolejki
/// <c>wikigen-send</c>, pobiera draft z bloba i uruchamia agenta Sender
/// który zapisuje strony wiki via <c>UpsertWikiPage()</c>.
/// </summary>
public class WikiDocSendFunction(
    WikiDocGenerationPipeline pipeline,
    IBlobMessageStore blobStore,
    ILogger<WikiDocSendFunction> logger)
{
    [Function(nameof(WikiDocSendFunction))]
    public async Task Run(
        [ServiceBusTrigger("wikigen-send", Connection = "ServiceBus")]
        BlobRefMessage message,
        CancellationToken ct)
    {
        logger.LogInformation("[SEND-FUNC] Downloading draft from '{Uri}'...", message.BlobUri);
        var draft = await blobStore.DownloadAsync<WikiDraft>(message.BlobUri, ct);

        logger.LogInformation("[SEND-FUNC] Sending {Count} edit(s) to wiki...", draft.Edits.Count);
        await pipeline.RunSenderAsync(draft);

        logger.LogInformation("[SEND-FUNC] Done. {Count} page(s) written.", draft.Edits.Count);
    }
}
