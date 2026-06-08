using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.Core.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;

public class GetWikiPageDetailsTool(
    IAzureDevOpsService devOpsService,
    ILogger<GetWikiPageDetailsTool> logger)
{
    [AgentTool(Description = """
        Retrieves the FULL content of a specific WIKI page by its wikiId and path.
        Use this after SearchWikiTool returns a short excerpt and you need the
        complete Markdown content of the most relevant pages (typically top 2-3)
        to verify architecture decisions, ADRs or technical constraints that
        may conflict with or depend on the analyzed work item.
        Returns: page id, path, full Markdown content and remote URL.
        """)]
    public async Task<string> GetWikiPageDetailsAsync(
        [Description("The wiki identifier (wikiId) as returned by SearchWikiTool")]
        string wikiId,
        [Description("The full page path (e.g. '/Architecture/ADR-012-Auth') as returned by SearchWikiTool")]
        string path)
    {
        logger.LogInformation("[TOOL] GetWikiPageDetails called — wikiId={WikiId}, path={Path}", wikiId, path);

        WikiPageDetail page;

        try
        {
            page = await devOpsService.GetWikiPageAsync(wikiId, path);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "[TOOL] GetWikiPageDetails — failed to fetch page '{Path}' from wiki '{WikiId}'", path, wikiId);
            return JsonSerializer.Serialize(new
            {
                error = $"Failed to fetch wiki page '{path}': {ex.Message}",
                wikiId,
                path
            });
        }

        if (string.IsNullOrWhiteSpace(page.Content))
        {
            logger.LogInformation("[TOOL] GetWikiPageDetails — page '{Path}' has no content (folder/container page)", path);
            return JsonSerializer.Serialize(new
            {
                wikiId,
                path = page.Path ?? path,
                content = (string?)null,
                note = "Page has no content (may be a container/folder page)."
            });
        }

        logger.LogInformation("[TOOL] GetWikiPageDetails — page '{Path}' fetched, content length={ContentLength}",
            page.Path, page.Content.Length);

        return JsonSerializer.Serialize(new
        {
            id = page.Id,
            wikiId,
            path = page.Path,
            content = page.Content,
            url = page.RemoteUrl
        });
    }
}
