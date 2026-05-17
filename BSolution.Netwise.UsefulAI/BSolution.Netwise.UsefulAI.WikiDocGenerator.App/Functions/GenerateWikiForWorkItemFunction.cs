using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Functions;

/// <summary>
/// HTTP trigger uruchamiany ręcznie. Generuje / aktualizuje stronę wiki dla
/// pojedynczego work itemu (zwykle Feature / PBI).
///
/// Użycie:
///   POST /api/workitems/{workItemId}/generate-wiki
///   body (opcjonalny): { "preferredPagePath": "/Architecture/Foo", "repositoryId": "..." }
/// </summary>
public class GenerateWikiForWorkItemFunction
{
    private readonly ILogger<GenerateWikiForWorkItemFunction> _logger;
    private readonly WikiDocGenerationPipeline _pipeline;

    public GenerateWikiForWorkItemFunction(
        ILogger<GenerateWikiForWorkItemFunction> logger,
        WikiDocGenerationPipeline pipeline)
    {
        _logger = logger;
        _pipeline = pipeline;
    }

    public record GenerateWikiRequestBody(string? PreferredPagePath, string? RepositoryId);

    [Function(nameof(GenerateWikiForWorkItemFunction))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "workitems/{workItemId:int}/generate-wiki")]
        HttpRequest req,
        int workItemId,
        CancellationToken ct)
    {
        if (workItemId <= 0)
            return new BadRequestObjectResult("Invalid work item id.");

        GenerateWikiRequestBody? body = null;
        if (req.ContentLength is > 0)
        {
            try
            {
                body = await req.ReadFromJsonAsync<GenerateWikiRequestBody>(ct);
            }
            catch
            {
                // Body opcjonalne — ignorujemy parse error i jedziemy z defaultami.
                body = null;
            }
        }

        var request = new WikiGenerationRequest(
            WorkItemId: workItemId,
            PreferredPagePath: body?.PreferredPagePath,
            RepositoryId: body?.RepositoryId);

        _logger.LogInformation(
            "[GENERATE-WIKI-FUNC] Generating wiki for WI#{WorkItemId} (page: {Path})",
            workItemId, request.PreferredPagePath ?? "<auto>");

        try
        {
            var summary = await _pipeline.RunForWorkItemAsync(request, ct);
            return new ContentResult
            {
                Content = summary,
                ContentType = "application/json",
                StatusCode = StatusCodes.Status200OK
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GENERATE-WIKI-FUNC] Pipeline failed for WI#{WorkItemId}.", workItemId);
            return new ObjectResult($"Pipeline execution failed: {ex.Message}")
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
