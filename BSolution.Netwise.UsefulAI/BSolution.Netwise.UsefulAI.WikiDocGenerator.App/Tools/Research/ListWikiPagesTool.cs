using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Services;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

/// <summary>
/// Listuje strony w docelowym (generowanym) wiki. Researcher porównuje listę z
/// obszarami zmienionymi w PR i wybiera, które aktualizować.
/// </summary>
public class ListWikiPagesTool(IWikiStore wikiStore)
{
    [AgentTool(Description = """
        Returns the flat list of all page paths in the target (generated) wiki.
        Use this to find pages whose path / name matches the changed area of code.
        """)]
    public async Task<string> ListWikiPagesAsync()
    {
        var paths = await wikiStore.ListPagePathsAsync();
        return JsonSerializer.Serialize(new { count = paths.Count, paths });
    }
}
