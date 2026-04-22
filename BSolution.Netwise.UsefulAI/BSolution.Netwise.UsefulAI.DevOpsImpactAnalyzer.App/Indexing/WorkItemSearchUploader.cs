using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

/// <summary>
/// Serwis odpowiedzialny wyłącznie za upload (MergeOrUpload) gotowych dokumentów
/// do indeksu Azure AI Search <c>work-items-index</c>.
/// </summary>
public interface IWorkItemSearchUploader
{
    Task UploadAsync(IReadOnlyList<WorkItemIndexDocument> documents, CancellationToken ct = default);
}

public class WorkItemSearchUploader : IWorkItemSearchUploader
{
    private readonly SearchClient _searchClient;
    private readonly ILogger<WorkItemSearchUploader> _logger;

    private const string WorkItemsIndex = "work-items-index";
    private const int SearchUploadBatchSize = 500;

    public WorkItemSearchUploader(IConfiguration config, ILogger<WorkItemSearchUploader> logger)
    {
        _logger = logger;
        _searchClient = new SearchClient(
            new Uri(config["AzureSearch:Endpoint"]!),
            WorkItemsIndex,
            new AzureKeyCredential(config["AzureSearch:ApiKey"]!));
    }

    public async Task UploadAsync(
        IReadOnlyList<WorkItemIndexDocument> documents,
        CancellationToken ct = default)
    {
        if (documents.Count == 0) return;

        for (var i = 0; i < documents.Count; i += SearchUploadBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = documents.Skip(i).Take(SearchUploadBatchSize)
                .Select(ToSearchDocument)
                .ToList();

            var indexBatch = IndexDocumentsBatch.MergeOrUpload(batch);

            await _searchClient.IndexDocumentsAsync(
                indexBatch,
                new IndexDocumentsOptions { ThrowOnAnyError = true },
                ct);

            _logger.LogInformation(
                "[WI-UPLOAD] Uploaded {N} document(s) to '{Index}'.",
                batch.Count, WorkItemsIndex);
        }
    }

    private static SearchDocument ToSearchDocument(WorkItemIndexDocument d)
    {
        var doc = new SearchDocument
        {
            ["id"] = d.Id,
            ["title"] = d.Title ?? string.Empty,
            ["type"] = d.Type ?? string.Empty,
            ["state"] = d.State ?? string.Empty,
            ["areaPath"] = d.AreaPath ?? string.Empty,
            ["tags"] = d.Tags ?? string.Empty,
            ["url"] = d.Url ?? string.Empty,
            ["contentVector"] = d.ContentVector
        };

        if (d.Description is not null) doc["description"] = d.Description;
        if (d.AcceptanceCriteria is not null) doc["acceptanceCriteria"] = d.AcceptanceCriteria;
        if (d.Comments is not null) doc["comments"] = d.Comments;
        if (d.ChangedDate.HasValue) doc["changedDate"] = d.ChangedDate.Value;

        return doc;
    }
}
