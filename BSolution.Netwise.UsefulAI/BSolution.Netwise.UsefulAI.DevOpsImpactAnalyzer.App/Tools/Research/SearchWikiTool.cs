using BSolution.Netwise.UsefulAI.Core.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;

public class SearchWikiTool(
    IEmbeddingService embeddingService,
    IAzureSearchService searchService,
    ILogger<SearchWikiTool> logger)
{
    private const string WikiIndex = "wiki-pages-index";

    [AgentTool(Description = """
        Searches WIKI pages for content related to the given query.
        Use this to find:
        - Architecture Decision Records (ADRs)
        - System architecture documentation  
        - Feature descriptions and business rules
        - Technical guidelines and constraints
        - Integration documentation
        Returns page titles, paths, content excerpts and relevance scores.
        """)]
    public async Task<string> SearchWikiAsync(
        [Description("Natural language search query describing what to look for in WIKI")]
        string query,

        [Description("Minimum similarity score between 0.0 and 1.0. Default: 0.65 " +
                     "(lower than work items — WIKI uses broader language)")]
        double minSimilarity = 0.65,

        [Description("Maximum number of results to return. Default: 5")]
        int maxResults = 5)
    {
        logger.LogInformation("[TOOL] SearchWiki called — query='{Query}', minSimilarity={MinSimilarity}, maxResults={MaxResults}",
            query, minSimilarity, maxResults);

        var embedding = await embeddingService.GetEmbeddingAsync(query);

        var results = await searchService.HybridSearchAsync(
            indexName: WikiIndex,
            vector: embedding,
            filter: null,
            top: maxResults,
            minScore: minSimilarity);

        logger.LogInformation("[TOOL] SearchWiki — found {Count} result(s) for query='{Query}'", results.Count, query);

        if (results.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                query,
                message = "No WIKI pages found matching this query",
                results = Array.Empty<object>()
            });
        }

        return JsonSerializer.Serialize(new
        {
            query,
            totalFound = results.Count,
            results = results.Select(r => new
            {
                title = r.Title,
                path = r.Path,
                wikiId = r.WikiId,
                similarityScore = Math.Round(r.Score, 3),
                contentExcerpt = TruncateText(r.ContentExcerpt, 500),
                url = r.Url
            })
        });
    }

    private static string? TruncateText(string? text, int maxLength)
    {
        if (text is null || text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }
}