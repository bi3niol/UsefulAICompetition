using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;

/// <summary>
/// Trzyma wygenerowane raporty Impact Analysis (markdown) w kontenerze <c>reports</c>
/// pod nazwą <c>{workItemId}.md</c> — deterministyczna nazwa, najnowszy raport
/// nadpisuje poprzedni.
/// </summary>
public interface IReportStore
{
    /// <summary>Zapisuje raport (markdown) dla danego work itemu. Zwraca URI bloba.</summary>
    Task<string> SaveAsync(int workItemId, string reportMarkdown, CancellationToken ct = default);

    /// <summary>Zwraca treść raportu lub <c>null</c> jeśli raport jeszcze nie został wygenerowany.</summary>
    Task<string?> TryGetAsync(int workItemId, CancellationToken ct = default);
}

public class ReportStore : IReportStore
{
    /// <summary>Nazwa kontenera dla wygenerowanych raportów Impact Analysis.</summary>
    public const string ContainerName = "reports";

    private const string ContentType = "text/markdown; charset=utf-8";

    private readonly BlobContainerClient _container;
    private readonly ILogger<ReportStore> _logger;
    private int _containerReady;

    public ReportStore(BlobServiceClient blobServiceClient, ILogger<ReportStore> logger)
    {
        _container = blobServiceClient.GetBlobContainerClient(ContainerName);
        _logger = logger;
    }

    public async Task<string> SaveAsync(int workItemId, string reportMarkdown, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);

        var blob = _container.GetBlobClient(BlobPaths.Report(workItemId));
        await blob.UploadAsync(
            BinaryData.FromString(reportMarkdown),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = ContentType } },
            cancellationToken: ct);

        _logger.LogInformation(
            "[REPORT-STORE] Saved report for WI#{WorkItemId} ({Bytes} B) → {Uri}",
            workItemId, reportMarkdown.Length, blob.Uri);

        return blob.Uri.ToString();
    }

    public async Task<string?> TryGetAsync(int workItemId, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(BlobPaths.Report(workItemId));
        try
        {
            var response = await blob.DownloadContentAsync(ct);
            return response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _containerReady, 1, 0) == 0)
            await _container.CreateIfNotExistsAsync(cancellationToken: ct);
    }
}
