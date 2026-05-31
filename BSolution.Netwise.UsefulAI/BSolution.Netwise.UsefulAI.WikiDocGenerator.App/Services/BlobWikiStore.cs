using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BSolution.Netwise.UsefulAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Services;

/// <summary>
/// Abstrakcja dostępu do wiki — docelowo DevOps Wiki API, tymczasowo Blob Storage.
/// Przełączane feature flagą <c>WikiDocGenerator:UseBlobStorage</c>.
/// </summary>
public interface IWikiStore
{
    Task<List<string>> ListPagePathsAsync(CancellationToken ct = default);
    Task<WikiPageDetail> GetPageAsync(string path, CancellationToken ct = default);
    Task<WikiPageWriteResult> UpsertPageAsync(string path, string markdownContent, string? eTag = null, CancellationToken ct = default);
}

/// <summary>
/// Tymczasowa implementacja wiki store oparta o Blob Storage.
/// Używana gdy brak uprawnień do DevOps Wiki API (feature flag UseBlobStorage=true).
/// </summary>
public class BlobWikiStore : IWikiStore
{
    private const string ContainerName = "wiki-pages";
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobWikiStore> _logger;

    public BlobWikiStore(BlobServiceClient blobService, ILogger<BlobWikiStore> logger)
    {
        _container = blobService.GetBlobContainerClient(ContainerName);
        _container.CreateIfNotExists();
        _logger = logger;
    }

    public async Task<List<string>> ListPagePathsAsync(CancellationToken ct = default)
    {
        var paths = new List<string>();
        await foreach (var blob in _container.GetBlobsAsync(cancellationToken: ct))
        {
            // Blob name: "Architecture/Module.md" -> wiki path: "/Architecture/Module"
            var path = "/" + blob.Name;
            if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                path = path[..^3];
            paths.Add(path);
        }
        return paths;
    }

    public async Task<WikiPageDetail> GetPageAsync(string path, CancellationToken ct = default)
    {
        var blobName = PathToBlobName(path);
        var blobClient = _container.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync(ct))
        {
            return new WikiPageDetail
            {
                Path = path,
                Content = null,
                ETag = null
            };
        }

        var response = await blobClient.DownloadContentAsync(ct);
        return new WikiPageDetail
        {
            Id = blobName,
            Path = path,
            Content = response.Value.Content.ToString(),
            ETag = response.Value.Details.ETag.ToString()
        };
    }

    public async Task<WikiPageWriteResult> UpsertPageAsync(string path, string markdownContent, string? eTag = null, CancellationToken ct = default)
    {
        var blobName = PathToBlobName(path);
        var blobClient = _container.GetBlobClient(blobName);

        var options = new BlobUploadOptions();
        //etag is not suppoerted now
        //if (!string.IsNullOrWhiteSpace(eTag))
        //{
        //    options.Conditions = new BlobRequestConditions
        //    {
        //        IfMatch = new Azure.ETag(eTag)
        //    };
        //}

        var existed = await blobClient.ExistsAsync(ct);

        var result = await blobClient.UploadAsync(
            BinaryData.FromString(markdownContent),
            options,
            ct);

        _logger.LogInformation("Wiki page {Action}: {Path}", existed ? "updated" : "created", path);

        return new WikiPageWriteResult
        {
            Path = path,
            ETag = result.Value.ETag.ToString(),
            Created = !existed
        };
    }

    private static string PathToBlobName(string path)
    {
        // "/Architecture/Module" -> "Architecture/Module.md"
        var normalized = path.TrimStart('/');
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            normalized += ".md";
        return normalized;
    }
}
