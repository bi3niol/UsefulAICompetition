using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;

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
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobMessageStore> _logger;

    // Lazy guard — CreateIfNotExists jest idempotentne, ale nie potrzeba go przy każdym upload.
    private int _containerReady;

    public BlobMessageStore(BlobContainerClient container, ILogger<BlobMessageStore> logger)
    {
        _container = container;
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
        // Wyciągamy nazwę bloba z pełnego URI i pobieramy przez klienta kontenera —
        // ta sama instancja credential co przy upload, bez tworzenia nowego BlobClient.
        var blobName = new Uri(blobUri).AbsolutePath.TrimStart('/').Substring(_container.Name.Length + 1);
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

/// <summary>
/// Generuje ścieżki blobów wg konwencji:
/// <c>{subfolder}/{yyyy-MM-dd}/{identity}_{guid8}.json</c>
///
/// Subfoldery odpowiadają nazwom kolejek Service Bus do których trafia dana wiadomość.
/// Guid8 (8 hex) = unikatowość przy wielokrotnych re-indeksowaniach tej samej treści.
/// </summary>
public static class BlobPaths
{
    private static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");
    private static string Uid   => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Blob dla <c>WorkItemDetailMessage</c> na kolejce <c>workitem-details</c>.</summary>
    public static string WorkItemDetail(int wiId) =>
        $"workitem-details/{Today}/{wiId}_{Uid}.json";

    /// <summary>Blob dla listy wszystkich chunków <c>WorkItemIndexDocument</c> jednego WI na kolejce <c>workitem-documents</c>.</summary>
    public static string WorkItemDocument(int wiId) =>
        $"workitem-documents/{Today}/{wiId}_{Uid}.json";

    /// <summary>Blob dla <c>WikiPageContentMessage</c> na kolejce <c>wiki-pages</c>.</summary>
    public static string WikiPage(string wikiId, string path) =>
        $"wiki-pages/{Today}/{Slug(wikiId)}-{Slug(path)}_{Uid}.json";

    /// <summary>Blob dla listy wszystkich chunków <c>WikiIndexDocument</c> jednej strony na kolejce <c>wiki-documents</c>.</summary>
    public static string WikiDocument(string wikiId, string path) =>
        $"wiki-documents/{Today}/{Slug(wikiId)}-{Slug(path)}_{Uid}.json";

    // Sanityzacja ścieżek WIKI: / → -, tylko [a-zA-Z0-9-], max 50 znaków.
    private static string Slug(string s) =>
        Regex.Replace(s.Replace('/', '-').Replace('_', '-').TrimStart('-'), @"[^a-zA-Z0-9\-]", "")
             is { Length: > 0 } slug
             ? slug[..Math.Min(slug.Length, 50)]
             : "x";
}
