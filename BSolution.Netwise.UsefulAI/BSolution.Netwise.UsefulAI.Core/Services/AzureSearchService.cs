using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using BSolution.Netwise.UsefulAI.Core.Models;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;

namespace BSolution.Netwise.UsefulAI.Core.Services;

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
    // SearchClient is thread-safe — we reuse instances per indexName instead of creating per call
    private readonly ConcurrentDictionary<string, SearchClient> _clients = new();
    private readonly Uri _endpoint;
    private readonly SearchClientOptions _clientOptions;
    private readonly TokenCredential? _tokenCredential;
    private readonly AzureKeyCredential? _keyCredential;

    private const string VectorField = "contentVector";
    private const string SemanticConfig = "devops-semantic-config";

    public AzureSearchService(IConfiguration config)
    {
        _endpoint = new Uri(config["AzureSearch:Endpoint"]!);
        _clientOptions = new SearchClientOptions();

        // Keyless (DefaultAzureCredential) — requires the "Search Index Data Reader" role on the MI.
        // Fall back to ApiKey if configured (local tests without MI).
        var apiKey = config["AzureSearch:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            _tokenCredential = new DefaultAzureCredential();
        else
            _keyCredential = new AzureKeyCredential(apiKey);
    }

    public async Task<List<SearchResultItem>> HybridSearchAsync(
        string indexName,
        float[] vector,
        string? filter = null,
        int top = 10,
        double minScore = 0.70,
        CancellationToken ct = default)
    {
        var client = _clients.GetOrAdd(indexName, name =>
            _tokenCredential is not null
                ? new SearchClient(_endpoint, name, _tokenCredential!, _clientOptions)
                : new SearchClient(_endpoint, name, _keyCredential!, _clientOptions));

        var searchOptions = new SearchOptions
        {
            Filter = filter,
            Size = top,

            // Required for SemanticSearch (caption/answer/reranker) to work
            QueryType = SearchQueryType.Semantic,

            // Hybrid search: vector + keyword (BM25) + semantic reranker
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(vector)
                    {
                        Fields = { VectorField },
                        KNearestNeighborsCount = top * 2  // fetch more, filter by score
                    }
                }
            },

            // Semantic reranker — improves result ranking
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = SemanticConfig,
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                QueryAnswer = new QueryAnswer(QueryAnswerType.Extractive)
            },

            // Projection disabled — both indexes have different fields; Select would cause an error
            // for fields that do not exist in a given index
        };

        // Run hybrid search (empty keyword query = vector + semantic only)
        var response = await client.SearchAsync<SearchDocument>("*", searchOptions, ct);

        var results = new List<SearchResultItem>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            // Filter out results below the similarity threshold
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
