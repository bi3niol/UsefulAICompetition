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
    // SearchClient jest thread-safe — reużywamy instancje per indexName zamiast tworzyć per wywołanie
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

        // Keyless (DefaultAzureCredential) — wymaga roli "Search Index Data Reader" na MI.
        // Fallback na ApiKey jeśli skonfigurowany (lokalne testy bez MI).
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

            // Wymagane, aby SemanticSearch (caption/answer/reranker) zadziałał
            QueryType = SearchQueryType.Semantic,

            // Hybrid search: vector + keyword (BM25) + semantic reranker
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(vector)
                    {
                        Fields = { VectorField },
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

            // Projekcja wyłączona — oba indeksy mają różne pola, Select spowodowałby błąd
            // dla pól nieistniejących w danym indeksie
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
