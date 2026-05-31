using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Services;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

/// <summary>
/// Pobiera pełną zawartość strony wiki wraz z ETagiem (niezbędny do późniejszej
/// aktualizacji przez Sender).
/// </summary>
public class GetWikiPageTool(IWikiStore wikiStore)
{
    [AgentTool(Description = """
        Reads full markdown content and ETag of a wiki page in the target (generated)
        wiki. Carry the returned ETag into the writer's edit so the upsert can succeed.
        """)]
    public async Task<string> GetWikiPageAsync(
        [Description("Wiki page path, e.g. /Architecture/Module")] string path)
    {
        var page = await wikiStore.GetPageAsync(path);
        return JsonSerializer.Serialize(new
        {
            path = page.Path,
            eTag = page.ETag,
            exists = page.Id is not null,
            content = page.Content
        });
    }
}
