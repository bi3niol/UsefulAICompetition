using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

/// <summary>
/// Serwis odpowiedzialny wyłącznie za upload (MergeOrUpload) gotowych dokumentów
/// do indeksu Azure AI Search <c>wiki-pages-index</c>.
/// </summary>
public interface IWikiSearchUploader
{
    Task UploadAsync(IReadOnlyList<WikiIndexDocument> documents, CancellationToken ct = default);
}

public class WikiSearchUploader : IWikiSearchUploader
{
    private readonly SearchClient _searchClient;
    private readonly ILogger<WikiSearchUploader> _logger;

    private const string WikiIndex = "wiki-pages-index";
    private const int SearchUploadBatchSize = 500;

    public WikiSearchUploader(IConfiguration config, ILogger<WikiSearchUploader> logger)
    {
        _logger = logger;
        _searchClient = new SearchClient(
            new Uri(config["AzureSearch:Endpoint"]!),
            WikiIndex,
            new AzureKeyCredential(config["AzureSearch:ApiKey"]!));
    }

    public async Task UploadAsync(
        IReadOnlyList<WikiIndexDocument> documents,
        CancellationToken ct = default)
    {
        if (documents.Count == 0) return;

        for (var i = 0; i < documents.Count; i += SearchUploadBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = documents.Skip(i).Take(SearchUploadBatchSize)
                .Select(ToSearchDocument)
                .ToList();

            await _searchClient.IndexDocumentsAsync(
                IndexDocumentsBatch.MergeOrUpload(batch),
                new IndexDocumentsOptions { ThrowOnAnyError = true },
                ct);

            _logger.LogInformation(
                "[WIKI-UPLOAD] Uploaded {N} document(s) to '{Index}'.",
                batch.Count, WikiIndex);
        }
    }

    private static SearchDocument ToSearchDocument(WikiIndexDocument d) => new()
    {
        ["id"] = d.Id,
        ["title"] = d.Title ?? string.Empty,
        ["path"] = d.Path ?? string.Empty,
        ["wikiId"] = d.WikiId ?? string.Empty,
        ["content"] = d.Content ?? string.Empty,
        ["contentExcerpt"] = d.ContentExcerpt ?? string.Empty,
        ["url"] = d.Url ?? string.Empty,
        ["contentVector"] = d.ContentVector
    };
}
