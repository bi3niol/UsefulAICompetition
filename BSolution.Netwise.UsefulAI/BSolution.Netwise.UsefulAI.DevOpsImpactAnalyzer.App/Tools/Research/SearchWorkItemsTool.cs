using BSolution.Netwise.UsefulAI.Core.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;

public class SearchWorkItemsTool(
    IEmbeddingService embeddingService,
    IAzureSearchService searchService)
{
    private const string WorkItemsIndex = "work-items-index";

    [AgentTool(Description = """
        Searches existing work items (User Stories, PBIs, Bugs, Tasks, Epics, Features)
        that are semantically related to the given query.
        Use multiple queries with different angles for comprehensive coverage:
        - functional description ("what the feature does")
        - domain/area ("which system area is affected")
        - user perspective ("who uses this")
        - technical terms ("how it might be implemented")
        Returns items ranked by semantic similarity with type, state and description.
        """)]
    public async Task<string> SearchWorkItemsAsync(
        [Description("Natural language search query describing what to look for")]
        string query,

        [Description("Filter by item type. Options: 'User Story', 'Product Backlog Item', " +
                     "'Bug', 'Task', 'Epic', 'Feature', 'Requirement', 'all'. Default: 'all'")]
        string itemType = "all",

        [Description("Minimum similarity score threshold between 0.0 and 1.0. Default: 0.70")]
        double minSimilarity = 0.70,

        [Description("Maximum number of results to return. Default: 10")]
        int maxResults = 10)
    {
        // Budujemy filter OData dla Azure AI Search
        string? filter = itemType != "all"
            ? $"type eq '{itemType}'"
            : null;

        // Generujemy embedding dla query
        var embedding = await embeddingService.GetEmbeddingAsync(query);

        // Hybrid search: vector + keyword + semantic reranker
        var results = await searchService.HybridSearchAsync(
            indexName: WorkItemsIndex,
            vector: embedding,
            filter: filter,
            top: maxResults,
            minScore: minSimilarity);

        if (results.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                query,
                message = "No work items found matching this query",
                results = Array.Empty<object>()
            });
        }

        return JsonSerializer.Serialize(new
        {
            query,
            totalFound = results.Count,
            results = results.Select(r => new
            {
                id = r.Id,
                title = r.Title,
                type = r.Type,
                state = r.State,
                similarityScore = Math.Round(r.Score, 3),
                descriptionShort = TruncateText(r.Description, 300),
                areaPath = r.AreaPath,
                tags = r.Tags,
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