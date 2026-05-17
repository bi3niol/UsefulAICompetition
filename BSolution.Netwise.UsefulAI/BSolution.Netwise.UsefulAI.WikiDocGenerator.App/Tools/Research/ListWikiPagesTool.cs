using BSolution.Netwise.UsefulAI.Core.Services;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

/// <summary>
/// Listuje strony w docelowym (generowanym) wiki. Researcher porównuje listę z
/// obszarami zmienionymi w PR i wybiera, które aktualizować.
/// </summary>
public class ListWikiPagesTool(IAzureDevOpsService devOps, IConfiguration config)
{
    [AgentTool(Description = """
        Returns the flat list of all page paths in the target (generated) wiki.
        Use this to find pages whose path / name matches the changed area of code.
        """)]
    public async Task<string> ListWikiPagesAsync()
    {
        var wikiId = config["WikiDocGenerator:TargetWikiId"]
            ?? throw new InvalidOperationException("WikiDocGenerator:TargetWikiId not configured.");

        var paths = await devOps.GetWikiPagePathsAsync(wikiId);
        return JsonSerializer.Serialize(new { wikiId, count = paths.Count, paths });
    }
}
