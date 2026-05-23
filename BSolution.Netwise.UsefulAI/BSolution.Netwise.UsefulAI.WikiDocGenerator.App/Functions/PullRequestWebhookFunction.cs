using BSolution.Netwise.UsefulAI.Core.Services;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Functions;

/// <summary>
/// Stage 1 — HTTP webhook konsumujący Azure DevOps Service Hook <c>git.pullrequest.merged</c>.
/// Parsuje payload, buduje <see cref="WikiGenPipelineMessage"/> i enqueue'uje na
/// <c>wikigen-pipeline</c>. Pipeline (ciężka praca LLM) wykonywana jest w Stage 2
/// (<see cref="WikiDocPipelineFunction"/>), dzięki czemu webhook odpowiada natychmiast.
/// </summary>
public class PullRequestWebhookFunction
{
    private readonly ILogger<PullRequestWebhookFunction> _logger;
    private readonly IAzureDevOpsService _devOps;

    public PullRequestWebhookFunction(
        ILogger<PullRequestWebhookFunction> logger,
        IAzureDevOpsService devOps)
    {
        _logger = logger;
        _devOps = devOps;
    }

    [Function(nameof(PullRequestWebhookFunction))]
    public async Task<PullRequestWebhookOutput> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "webhooks/pullrequest")]
        HttpRequest req,
        CancellationToken ct)
    {
        JsonObject? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<JsonObject>(req.Body, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[PR-WEBHOOK] Invalid JSON payload.");
            return new PullRequestWebhookOutput
            {
                HttpResponse = new BadRequestObjectResult("Invalid JSON payload.")
            };
        }

        var resource = payload?["resource"]?.AsObject();
        if (resource is null)
            return new PullRequestWebhookOutput
            {
                HttpResponse = new BadRequestObjectResult("Missing 'resource' in payload.")
            };

        var status = resource["status"]?.ToString();
        if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[PR-WEBHOOK] Ignoring PR event with status '{Status}'.", status);
            return new PullRequestWebhookOutput
            {
                HttpResponse = new OkObjectResult(new { ignored = true, reason = $"status={status}" })
            };
        }

        var prId = resource["pullRequestId"]?.GetValue<int>() ?? 0;
        var repoId = resource["repository"]?["id"]?.ToString();
        var repoName = resource["repository"]?["name"]?.ToString();

        if (prId <= 0 || string.IsNullOrEmpty(repoId))
            return new PullRequestWebhookOutput
            {
                HttpResponse = new BadRequestObjectResult("Missing pullRequestId / repository id.")
            };

        var linkedIds = await _devOps.GetPullRequestWorkItemIdsAsync(repoId!, prId, ct);

        var message = new WikiGenPipelineMessage
        {
            Source = WikiGenSource.PullRequest,
            RepositoryId = repoId,
            RepositoryName = repoName ?? string.Empty,
            PullRequestId = prId,
            PrTitle = resource["title"]?.ToString() ?? string.Empty,
            PrDescription = resource["description"]?.ToString(),
            SourceBranch = resource["sourceRefName"]?.ToString() ?? string.Empty,
            TargetBranch = resource["targetRefName"]?.ToString() ?? string.Empty,
            MergeCommitId = resource["lastMergeCommit"]?["commitId"]?.ToString(),
            Author = resource["createdBy"]?["displayName"]?.ToString(),
            LinkedWorkItemIds = linkedIds
        };

        _logger.LogInformation(
            "[PR-WEBHOOK] Enqueued pipeline message for PR#{Id} ({Repo}).",
            prId, repoName);

        return new PullRequestWebhookOutput
        {
            HttpResponse = new AcceptedResult(),
            ServiceBusMessage = message
        };
    }
}

/// <summary>
/// Multi-output binding: HTTP response + optional Service Bus message.
/// </summary>
public class PullRequestWebhookOutput
{
    [HttpResult]
    public IActionResult HttpResponse { get; set; } = new AcceptedResult();

    [ServiceBusOutput("wikigen-pipeline", Connection = "ServiceBus")]
    public WikiGenPipelineMessage? ServiceBusMessage { get; set; }
}
