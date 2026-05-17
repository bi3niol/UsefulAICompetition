using BSolution.Netwise.UsefulAI.Core.Services;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Functions;

/// <summary>
/// HTTP webhook konsumujący Azure DevOps Service Hook <c>git.pullrequest.merged</c>.
/// Po zakończonym merge'u uruchamia pipeline aktualizacji dokumentacji wiki.
///
/// Konfiguracja po stronie DevOps:
///   - Service hooks → Web Hooks → "Pull request merge attempted"
///   - Filter: "Status = succeeded"
///   - URL: POST {functionapp}/api/webhooks/pullrequest
/// </summary>
public class PullRequestWebhookFunction
{
    private readonly ILogger<PullRequestWebhookFunction> _logger;
    private readonly WikiDocGenerationPipeline _pipeline;
    private readonly IAzureDevOpsService _devOps;

    public PullRequestWebhookFunction(
        ILogger<PullRequestWebhookFunction> logger,
        WikiDocGenerationPipeline pipeline,
        IAzureDevOpsService devOps)
    {
        _logger = logger;
        _pipeline = pipeline;
        _devOps = devOps;
    }

    [Function(nameof(PullRequestWebhookFunction))]
    public async Task<IActionResult> Run(
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
            return new BadRequestObjectResult("Invalid JSON payload.");
        }

        var resource = payload?["resource"]?.AsObject();
        if (resource is null)
            return new BadRequestObjectResult("Missing 'resource' in payload.");

        var status = resource["status"]?.ToString();
        // Akceptujemy tylko PR-y zakończone merge'em — inne stany ignorujemy.
        if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[PR-WEBHOOK] Ignoring PR event with status '{Status}'.", status);
            return new OkObjectResult(new { ignored = true, reason = $"status={status}" });
        }

        var prId = resource["pullRequestId"]?.GetValue<int>() ?? 0;
        var repoId = resource["repository"]?["id"]?.ToString();
        var repoName = resource["repository"]?["name"]?.ToString();

        if (prId <= 0 || string.IsNullOrEmpty(repoId))
            return new BadRequestObjectResult("Missing pullRequestId / repository id.");

        var linkedIds = await _devOps.GetPullRequestWorkItemIdsAsync(repoId!, prId, ct);

        var prEvent = new PullRequestEvent(
            RepositoryId: repoId!,
            RepositoryName: repoName ?? string.Empty,
            PullRequestId: prId,
            Title: resource["title"]?.ToString() ?? string.Empty,
            Description: resource["description"]?.ToString(),
            SourceBranch: resource["sourceRefName"]?.ToString() ?? string.Empty,
            TargetBranch: resource["targetRefName"]?.ToString() ?? string.Empty,
            MergeCommitId: resource["lastMergeCommit"]?["commitId"]?.ToString(),
            Author: resource["createdBy"]?["displayName"]?.ToString(),
            LinkedWorkItemIds: linkedIds
        );

        try
        {
            var summary = await _pipeline.RunForPullRequestAsync(prEvent, ct);
            return new ContentResult
            {
                Content = summary,
                ContentType = "application/json",
                StatusCode = StatusCodes.Status200OK
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PR-WEBHOOK] Pipeline failed for PR#{Id}.", prId);
            return new ObjectResult($"Pipeline execution failed: {ex.Message}")
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
