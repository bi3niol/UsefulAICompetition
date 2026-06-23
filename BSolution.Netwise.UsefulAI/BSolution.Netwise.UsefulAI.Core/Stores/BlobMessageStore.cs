using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.Core.Stores;

/// <summary>
/// Service for storing large message payloads in Blob Storage (Claim-Check Pattern).
/// Container: <c>messages</c>. Subfolders per message type — see <see cref="BlobPaths"/>.
/// </summary>
public interface IBlobMessageStore
{
    /// <summary>Serializes payload to JSON, uploads the blob and returns its full URI.</summary>
    Task<string> UploadAsync<T>(string blobPath, T payload, CancellationToken ct = default);

    /// <summary>Downloads the blob from the given URI and deserializes it to <typeparamref name="T"/>.</summary>
    Task<T> DownloadAsync<T>(string blobUri, CancellationToken ct = default);
}

public class BlobMessageStore : IBlobMessageStore
{
    /// <summary>Container name for all Claim-Check Pattern payloads.</summary>
    public const string ContainerName = "messages";

    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobMessageStore> _logger;

    // Lazy guard — CreateIfNotExists is idempotent, but there is no need to call it on every upload.
    private int _containerReady;

    public BlobMessageStore(BlobServiceClient blobServiceClient, ILogger<BlobMessageStore> logger)
    {
        _container = blobServiceClient.GetBlobContainerClient(ContainerName);
        _logger = logger;
    }

    public async Task<string> UploadAsync<T>(string blobPath, T payload, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);

        var blob = _container.GetBlobClient(blobPath);
        var json = JsonSerializer.Serialize(payload);
        await blob.UploadAsync(BinaryData.FromString(json), overwrite: true, cancellationToken: ct);

        _logger.LogDebug("[BLOB-STORE] Uploaded '{BlobPath}' ({Bytes} B)", blobPath, json.Length);
        return blob.Uri.ToString();
    }

    public async Task<T> DownloadAsync<T>(string blobUri, CancellationToken ct = default)
    {
        // Extract the blob name from the full URI relative to the container URI —
        // same credential instance as at upload time, no new BlobClient needed.
        // Using _container.Uri works for both Azurite and production.
        var blobName = blobUri.Substring(_container.Uri.ToString().Length).TrimStart('/');
        var blob = _container.GetBlobClient(blobName);
        var response = await blob.DownloadContentAsync(ct);
        var json = response.Value.Content.ToString();

        return JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize blob payload: {blobUri}");
    }

    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _containerReady, 1, 0) == 0)
            await _container.CreateIfNotExistsAsync(cancellationToken: ct);
    }
}
