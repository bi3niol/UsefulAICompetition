using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Messages;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Functions;

/// <summary>
/// Stage 1 — HTTP trigger do ręcznego uruchomienia pełnego skanu kodu źródłowego.
/// Rozwiązuje repozytoria z konfiga, enqueue'uje po jednym
/// <see cref="WikiGenPipelineMessage"/> per repo na <c>wikigen-pipeline</c>.
/// Researcher w Stage 2 sam listuje pliki i generuje wiki.
/// </summary>
public class CodeScanInitFunction(
    CodeRepositoryResolver resolver,
    IOptions<CodeScanOptions> options,
    ILogger<CodeScanInitFunction> logger)
{
    [Function(nameof(CodeScanInitFunction))]
    public async Task<CodeScanInitOutput> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "codescan/init")]
        HttpRequest req,
        CancellationToken ct)
    {
        var repos = await resolver.ResolveAllAsync(ct);

        if (repos.Count == 0)
        {
            logger.LogWarning("[CODESCAN-INIT] No repositories configured in WikiDocGenerator:Code:Repositories.");
            return new CodeScanInitOutput
            {
                HttpResponse = new BadRequestObjectResult("No repositories configured for code scan.")
            };
        }

        var messages = repos.Select(r => new WikiGenPipelineMessage
        {
            Source = WikiGenSource.CodeScan,
            RepositoryId = r.Id,
            RepositoryName = r.Name,
            Branch = r.Branch,
            IsFullScan = true
        }).ToArray();

        logger.LogInformation(
            "[CODESCAN-INIT] Enqueued {Count} repo(s) for full code scan: {Names}.",
            messages.Length, string.Join(", ", repos.Select(r => r.Name)));

        return new CodeScanInitOutput
        {
            HttpResponse = new OkObjectResult(new
            {
                queued = messages.Length,
                repositories = repos.Select(r => new { r.Name, r.Branch })
            }),
            Messages = messages
        };
    }
}

public class CodeScanInitOutput
{
    [HttpResult]
    public required IActionResult HttpResponse { get; set; }

    [ServiceBusOutput("wikigen-pipeline", Connection = "ServiceBus")]
    public WikiGenPipelineMessage[]? Messages { get; set; }
}
