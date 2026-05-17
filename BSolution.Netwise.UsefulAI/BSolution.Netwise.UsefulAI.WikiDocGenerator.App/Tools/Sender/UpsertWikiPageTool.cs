using BSolution.Netwise.UsefulAI.Core.Services;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Sender;

/// <summary>
/// Zapisuje (tworzy lub aktualizuje) stronę w generowanym wiki. Używa
/// docelowego <c>TargetWikiId</c> z konfiguracji — generator NIGDY nie pisze
/// do wiki używanego przez Impact Analyzera.
/// </summary>
public class UpsertWikiPageTool(IAzureDevOpsService devOps, IConfiguration config)
{
    [AgentTool(Description = """
        Creates or updates a single page in the target (generated) wiki.
        If existingETag is provided, the page is updated with optimistic concurrency.
        If existingETag is empty / null, the page is treated as new (will be created).
        Call this EXACTLY ONCE per edit produced by the writer. Do not modify content.
        """)]
    public async Task<string> UpsertWikiPageAsync(
        [Description("Wiki page path, e.g. /Architecture/Module")] string path,
        [Description("Full markdown content for the page")] string markdownContent,
        [Description("ETag of the existing page for updates. Empty string for new pages.")]
        string existingETag)
    {
        if (string.IsNullOrWhiteSpace(markdownContent))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Markdown content cannot be empty."
            });
        }

        var wikiId = config["WikiDocGenerator:TargetWikiId"]
            ?? throw new InvalidOperationException("WikiDocGenerator:TargetWikiId not configured.");

        var eTag = string.IsNullOrWhiteSpace(existingETag) ? null : existingETag;

        var result = await devOps.CreateOrUpdateWikiPageAsync(wikiId, path, markdownContent, eTag);

        return JsonSerializer.Serialize(new
        {
            success = true,
            created = result.Created,
            path = result.Path,
            eTag = result.ETag,
            remoteUrl = result.RemoteUrl
        });
    }
}
