using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;

public interface IAzureSearchService
{
    Task<List<SearchResultItem>> HybridSearchAsync(
        string indexName,
        float[] vector,
        string? filter = null,
        int top = 10,
        double minScore = 0.70,
        CancellationToken ct = default);
}

public class AzureSearchService : IAzureSearchService
{
    private readonly string _endpoint;
    private readonly AzureKeyCredential _credential;

    // Nazwy pól w indeksach — muszą pasować do schematu indeksu
    private const string VectorFieldWorkItems = "contentVector";
    private const string VectorFieldWiki = "contentVector";
    private const string SemanticConfig = "devops-semantic-config";

    public AzureSearchService(IConfiguration config)
    {
        _endpoint = config["AzureSearch:Endpoint"]!;
        _credential = new AzureKeyCredential(config["AzureSearch:ApiKey"]!);
    }

    public async Task<List<SearchResultItem>> HybridSearchAsync(
        string indexName,
        float[] vector,
        string? filter = null,
        int top = 10,
        double minScore = 0.70,
        CancellationToken ct = default)
    {
        var client = new SearchClient(
            new Uri(_endpoint),
            indexName,
            _credential);

        var searchOptions = new SearchOptions
        {
            Filter = filter,
            Size = top,

            // Wymagane, aby SemanticSearch (caption/answer/reranker) zadziałał
            QueryType = SearchQueryType.Semantic,

            // Hybrid search: vector + keyword (BM25) + semantic reranker
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(vector)
                    {
                        Fields = { VectorFieldWorkItems },
                        KNearestNeighborsCount = top * 2  // pobieramy więcej, filtrujemy po score
                    }
                }
            },

            // Semantic reranker — poprawia ranking wyników
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = SemanticConfig,
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                QueryAnswer = new QueryAnswer(QueryAnswerType.Extractive)
            },

            // Pola które chcemy zwrócić (projekcja)
            //Select =
            //{
            //    "id", "title", "type", "state", "description",
            //    "acceptanceCriteria", "areaPath", "tags",
            //    "path", "wikiId", "contentExcerpt",
            //    "url", "createdDate", "changedDate"
            //}
        };

        // Uruchamiamy hybrid search (keyword query pusta = tylko vector + semantic)
        var response = await client.SearchAsync<SearchDocument>("*", searchOptions, ct);

        var results = new List<SearchResultItem>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            // Filtrujemy wyniki poniżej progu podobieństwa
            var score = result.Score ?? 0;
            if (score < minScore) continue;

            results.Add(MapToSearchResultItem(result.Document, score));
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(top)
            .ToList();
    }

    private static SearchResultItem MapToSearchResultItem(SearchDocument doc, double score)
    {
        return new SearchResultItem
        {
            Id = doc.TryGetValue("id", out var id) ? id?.ToString() : null,
            Title = doc.TryGetValue("title", out var t) ? t?.ToString() : null,
            Type = doc.TryGetValue("type", out var tp) ? tp?.ToString() : null,
            State = doc.TryGetValue("state", out var st) ? st?.ToString() : null,
            Description = doc.TryGetValue("description", out var d) ? d?.ToString() : null,
            AcceptanceCriteria = doc.TryGetValue("acceptanceCriteria", out var ac) ? ac?.ToString() : null,
            AreaPath = doc.TryGetValue("areaPath", out var ap) ? ap?.ToString() : null,
            Tags = doc.TryGetValue("tags", out var tg) ? tg?.ToString() : null,
            Path = doc.TryGetValue("path", out var p) ? p?.ToString() : null,
            WikiId = doc.TryGetValue("wikiId", out var wi) ? wi?.ToString() : null,
            ContentExcerpt = doc.TryGetValue("contentExcerpt", out var ce) ? ce?.ToString() : null,
            Url = doc.TryGetValue("url", out var u) ? u?.ToString() : null,
            Score = score
        };
    }
}