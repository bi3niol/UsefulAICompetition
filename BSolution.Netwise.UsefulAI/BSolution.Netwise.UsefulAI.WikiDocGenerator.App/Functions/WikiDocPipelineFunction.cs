using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Messages;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Functions;

/// <summary>
/// Stage 2 - Service Bus consumer on queue wikigen-pipeline.
/// Receives WikiGenPipelineMessage and runs the full LLM pipeline.
/// </summary>
public class WikiDocPipelineFunction(
    WikiDocGenerationPipeline pipeline,
    ILogger<WikiDocPipelineFunction> logger)
{
    [Function(nameof(WikiDocPipelineFunction))]
    public async Task Run(
        [ServiceBusTrigger("wikigen-pipeline", Connection = "ServiceBus")]
        WikiGenPipelineMessage message,
        CancellationToken ct)
    {
        switch (message.Source)
        {
            case WikiGenSource.PullRequest:
                await HandlePullRequestAsync(message, ct);
                break;

            case WikiGenSource.WorkItems:
                await HandleWorkItemsAsync(message, ct);
                break;

            case WikiGenSource.CodeScan:
                logger.LogWarning("[WIKIGEN-PIPELINE] CodeScan source not yet implemented.");
                break;

            default:
                logger.LogWarning("[WIKIGEN-PIPELINE] Unknown source '{Source}'.", message.Source);
                break;
        }
    }

    private async Task HandlePullRequestAsync(WikiGenPipelineMessage msg, CancellationToken ct)
    {
        logger.LogInformation(
            "[WIKIGEN-PIPELINE] Processing PR#{Id} from {Repo}.",
            msg.PullRequestId, msg.RepositoryName);

        var prEvent = new PullRequestEvent(
            RepositoryId: msg.RepositoryId ?? string.Empty,
            RepositoryName: msg.RepositoryName ?? string.Empty,
            PullRequestId: msg.PullRequestId ?? 0,
            Title: msg.PrTitle ?? string.Empty,
            Description: msg.PrDescription,
            SourceBranch: msg.SourceBranch ?? string.Empty,
            TargetBranch: msg.TargetBranch ?? string.Empty,
            MergeCommitId: msg.MergeCommitId,
            Author: msg.Author,
            LinkedWorkItemIds: msg.LinkedWorkItemIds
        );

        var result = await pipeline.RunForPullRequestAsync(prEvent, ct);
        logger.LogInformation("[WIKIGEN-PIPELINE] PR#{Id} done. Result: {Result}", msg.PullRequestId, result);
    }

    private async Task HandleWorkItemsAsync(WikiGenPipelineMessage msg, CancellationToken ct)
    {
        logger.LogInformation(
            "[WIKIGEN-PIPELINE] Processing {Count} work item(s): [{Ids}].",
            msg.WorkItemIds.Count, string.Join(", ", msg.WorkItemIds));

        var request = new WorkItemsWikiRefreshRequest(
            WorkItemIds: msg.WorkItemIds,
            RepositoryId: msg.RepositoryId);

        var result = await pipeline.RunForWorkItemsAsync(request, ct);
        logger.LogInformation("[WIKIGEN-PIPELINE] Work items batch done. Result: {Result}", result);
    }
}
