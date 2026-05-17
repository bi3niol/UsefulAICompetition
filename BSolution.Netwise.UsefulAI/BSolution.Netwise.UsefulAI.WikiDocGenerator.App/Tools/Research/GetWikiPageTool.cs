using BSolution.Netwise.UsefulAI.Core.Services;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

/// <summary>
/// Pobiera pełną zawartość strony wiki wraz z ETagiem (niezbędny do późniejszej
/// aktualizacji przez Sender).
/// </summary>
public class GetWikiPageTool(IAzureDevOpsService devOps, IConfiguration config)
{
    [AgentTool(Description = """
        Reads full markdown content and ETag of a wiki page in the target (generated)
        wiki. Carry the returned ETag into the writer's edit so the upsert can succeed.
        """)]
    public async Task<string> GetWikiPageAsync(
        [Description("Wiki page path, e.g. /Architecture/Module")] string path)
    {
        var wikiId = config["WikiDocGenerator:TargetWikiId"]
            ?? throw new InvalidOperationException("WikiDocGenerator:TargetWikiId not configured.");

        var page = await devOps.GetWikiPageAsync(wikiId, path);
        return JsonSerializer.Serialize(new
        {
            wikiId,
            path = page.Path,
            eTag = page.ETag,
            exists = page.Id is not null,
            content = page.Content
        });
    }
}
