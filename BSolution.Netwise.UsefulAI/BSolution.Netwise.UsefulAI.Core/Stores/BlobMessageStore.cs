using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.Core.Stores;

/// <summary>
/// Serwis do przechowywania dużych payloadów wiadomości w Blob Storage (Claim-Check Pattern).
/// Container: <c>messages</c>. Subfolders per typ wiadomości — patrz <see cref="BlobPaths"/>.
/// </summary>
public interface IBlobMessageStore
{
    /// <summary>Serializuje payload do JSON, wgrywa bloba i zwraca jego pełny URI.</summary>
    Task<string> UploadAsync<T>(string blobPath, T payload, CancellationToken ct = default);

    /// <summary>Pobiera bloba z podanego URI i deserializuje do <typeparamref name="T"/>.</summary>
    Task<T> DownloadAsync<T>(string blobUri, CancellationToken ct = default);
}

public class BlobMessageStore : IBlobMessageStore
{
    /// <summary>Nazwa kontenera dla wszystkich payloadów Claim-Check Pattern.</summary>
    public const string ContainerName = "messages";

    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobMessageStore> _logger;

    // Lazy guard — CreateIfNotExists jest idempotentne, ale nie potrzeba go przy każdym upload.
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
        // Wyciągamy nazwę bloba z pełnego URI względem URI kontenera —
        // ta sama instancja credential co przy upload, bez tworzenia nowego BlobClient.
        // Podejście przez _container.Uri działa zarówno dla Azurite jak i produkcji.
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
