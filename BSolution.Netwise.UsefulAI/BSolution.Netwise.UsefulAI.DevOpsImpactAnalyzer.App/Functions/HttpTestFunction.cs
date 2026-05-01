using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Functions;

public class HttpTestFunction
{
    private readonly ILogger<HttpTestFunction> _logger;
    private readonly SearchWikiTool _searchWikiTool;    

    public HttpTestFunction(ILogger<HttpTestFunction> logger, SearchWikiTool searchWikiTool)
    {
        _logger = logger;
        _searchWikiTool = searchWikiTool;
    }

    [Function("HttpTestFunction")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("integracje z systemem zewnętrznym");
        //return new OkObjectResult(await _searchWikiTool.SearchWikiAsync("integracje z systemem zewnętrznym"));
    }
}