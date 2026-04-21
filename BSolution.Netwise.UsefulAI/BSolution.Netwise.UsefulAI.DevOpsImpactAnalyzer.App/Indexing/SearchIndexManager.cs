using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

/// <summary>
/// Tworzy / aktualizuje schematy indeksów Azure AI Search przy starcie aplikacji.
/// Rejestrowany jako IHostedService — uruchamia się automatycznie przed pierwszym triggerem.
/// </summary>
public class SearchIndexManager : IHostedService
{
    private readonly SearchIndexClient _indexClient;
    private readonly ILogger<SearchIndexManager> _logger;

    // Wymiary wektora text-embedding-3-large
    private const int VectorDimensions = 3072;
    private const string VectorProfileName = "vector-profile";
    private const string AlgorithmName = "hnsw-config";
    private const string SemanticConfigName = "devops-semantic-config";

    public SearchIndexManager(IConfiguration config, ILogger<SearchIndexManager> logger)
    {
        _logger = logger;
        _indexClient = new SearchIndexClient(
            new Uri(config["AzureSearch:Endpoint"]!),
            new AzureKeyCredential(config["AzureSearch:ApiKey"]!));
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("[INDEX-MANAGER] Ensuring Azure AI Search indexes exist...");

        await EnsureIndexAsync(BuildWorkItemsIndex(), ct);
        await EnsureIndexAsync(BuildWikiPagesIndex(), ct);

        _logger.LogInformation("[INDEX-MANAGER] All indexes ready.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // ── Index Creation ────────────────────────────────────────────────────────

    private async Task EnsureIndexAsync(SearchIndex index, CancellationToken ct)
    {
        try
        {
            await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: ct);
            _logger.LogInformation("[INDEX-MANAGER] Index '{Name}' ready.", index.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INDEX-MANAGER] Failed to create/update index '{Name}'.", index.Name);
            throw;
        }
    }

    // ── work-items-index ─────────────────────────────────────────────────────

    private static SearchIndex BuildWorkItemsIndex() => new("work-items-index")
    {
        Fields =
        [
            new SimpleField("id", SearchFieldDataType.String)
                { IsKey = true, IsFilterable = true },

            new SearchableField("title")
                { IsFilterable = false, IsSortable = false },

            new SimpleField("type", SearchFieldDataType.String)
                { IsFilterable = true, IsFacetable = true },

            new SimpleField("state", SearchFieldDataType.String)
                { IsFilterable = true, IsFacetable = true },

            new SearchableField("description"),
            new SearchableField("acceptanceCriteria"),
            new SearchableField("comments"),

            new SimpleField("areaPath", SearchFieldDataType.String)
                { IsFilterable = true, IsFacetable = true },

            new SearchableField("tags"),

            new SimpleField("url", SearchFieldDataType.String),

            new SimpleField("changedDate", SearchFieldDataType.DateTimeOffset)
                { IsFilterable = true, IsSortable = true },

            // Pole wektora — 3072 wymiary dla text-embedding-3-large
            new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = VectorDimensions,
                VectorSearchProfileName = VectorProfileName
            }
        ],

        VectorSearch = new VectorSearch
        {
            Profiles = { new VectorSearchProfile(VectorProfileName, AlgorithmName) },
            Algorithms =
            {
                new HnswAlgorithmConfiguration(AlgorithmName)
                {
                    Parameters = new HnswParameters
                    {
                        Metric = VectorSearchAlgorithmMetric.Cosine,
                        M = 4,
                        EfConstruction = 400,
                        EfSearch = 500
                    }
                }
            }
        },

        SemanticSearch = new SemanticSearch
        {
            Configurations =
            {
                new SemanticConfiguration(SemanticConfigName, new SemanticPrioritizedFields
                {
                    TitleField = new SemanticField("title"),
                    ContentFields =
                    {
                        new SemanticField("description"),
                        new SemanticField("acceptanceCriteria"),
                        new SemanticField("comments")
                    },
                    KeywordsFields =
                    {
                        new SemanticField("tags"),
                        new SemanticField("areaPath")
                    }
                })
            }
        }
    };

    // ── wiki-pages-index ─────────────────────────────────────────────────────

    private static SearchIndex BuildWikiPagesIndex() => new("wiki-pages-index")
    {
        Fields =
        [
            new SimpleField("id", SearchFieldDataType.String)
                { IsKey = true, IsFilterable = true },

            new SearchableField("title"),

            new SimpleField("path", SearchFieldDataType.String)
                { IsFilterable = true, IsSortable = true },

            new SimpleField("wikiId", SearchFieldDataType.String)
                { IsFilterable = true },

            new SearchableField("contentExcerpt"),
            new SearchableField("content"),

            new SimpleField("url", SearchFieldDataType.String),

            new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = VectorDimensions,
                VectorSearchProfileName = VectorProfileName
            }
        ],

        VectorSearch = new VectorSearch
        {
            Profiles = { new VectorSearchProfile(VectorProfileName, AlgorithmName) },
            Algorithms =
            {
                new HnswAlgorithmConfiguration(AlgorithmName)
                {
                    Parameters = new HnswParameters
                    {
                        Metric = VectorSearchAlgorithmMetric.Cosine,
                        M = 4,
                        EfConstruction = 400,
                        EfSearch = 500
                    }
                }
            }
        },

        SemanticSearch = new SemanticSearch
        {
            Configurations =
            {
                new SemanticConfiguration(SemanticConfigName, new SemanticPrioritizedFields
                {
                    TitleField = new SemanticField("title"),
                    ContentFields =
                    {
                        new SemanticField("content"),
                        new SemanticField("contentExcerpt")
                    },
                    KeywordsFields =
                    {
                        new SemanticField("path")
                    }
                })
            }
        }
    };
}