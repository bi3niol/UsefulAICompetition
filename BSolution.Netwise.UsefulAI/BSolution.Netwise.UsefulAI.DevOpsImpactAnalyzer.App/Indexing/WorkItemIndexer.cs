using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

public interface IWorkItemIndexer
{
    /// <summary>Pełna synchronizacja — indeksuje wszystkie work itemy z DevOps.</summary>
    Task RunFullSyncAsync(CancellationToken ct = default);

    /// <summary>Synchronizacja przyrostowa — tylko elementy zmienione od <paramref name="since"/>.</summary>
    Task RunIncrementalSyncAsync(DateTime since, CancellationToken ct = default);
}

public class WorkItemIndexer : IWorkItemIndexer
{
    private readonly IAzureDevOpsService _devOps;
    private readonly IEmbeddingService _embedding;
    private readonly SearchClient _searchClient;
    private readonly ILogger<WorkItemIndexer> _logger;

    private const string WorkItemsIndex = "work-items-index";

    // ~2000 tokenów (średnio 4 znaki/token)
    private const int MaxChunkChars = 8_000;

    // Limity Azure Search i DevOps API
    private const int SearchUploadBatchSize = 500;
    private const int DevOpsBatchSize = 200;

    // Throttling wywołań Embedding API
    private const int MaxParallelEmbeddings = 4;

    private static readonly string[] IndexableTypes =
    [
        "User Story", "Product Backlog Item", "Bug",
        "Task", "Epic", "Feature", "Requirement"
    ];

    public WorkItemIndexer(
        IAzureDevOpsService devOps,
        IEmbeddingService embedding,
        IConfiguration config,
        ILogger<WorkItemIndexer> logger)
    {
        _devOps = devOps;
        _embedding = embedding;
        _logger = logger;

        _searchClient = new SearchClient(
            new Uri(config["AzureSearch:Endpoint"]!),
            WorkItemsIndex,
            new AzureKeyCredential(config["AzureSearch:ApiKey"]!));
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public Task RunFullSyncAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[INDEXER] Starting FULL sync...");
        return SyncByWiqlAsync(since: null, ct);
    }

    public Task RunIncrementalSyncAsync(DateTime since, CancellationToken ct = default)
    {
        _logger.LogInformation("[INDEXER] Starting INCREMENTAL sync since {Since:O}...", since);
        return SyncByWiqlAsync(since, ct);
    }

    // ── Core Sync Pipeline ───────────────────────────────────────────────────

    private async Task SyncByWiqlAsync(DateTime? since, CancellationToken ct)
    {
        // Krok 1: pobierz ID przez WIQL
        var wiql = BuildWiqlQuery(since);
        var ids = await _devOps.QueryWorkItemIdsAsync(wiql, ct);

        _logger.LogInformation("[INDEXER] WIQL returned {Count} work item(s).", ids.Count);

        if (ids.Count == 0) return;

        // Krok 2: batch fetch + embed + upload (po DevOpsBatchSize ID naraz)
        for (var i = 0; i < ids.Count; i += DevOpsBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = ids.Skip(i).Take(DevOpsBatchSize).ToList();
            _logger.LogInformation("[INDEXER] Processing IDs {From}–{To} of {Total}...",
                i + 1, i + batch.Count, ids.Count);

            var workItems = await _devOps.GetWorkItemsBatchAsync(batch, ct);
            var documents = await BuildSearchDocumentsAsync(workItems, ct);

            await UploadToSearchAsync(documents, ct);
        }

        _logger.LogInformation("[INDEXER] Sync complete. Total IDs processed: {Total}.", ids.Count);
    }

    // ── Embedding + Document Building ────────────────────────────────────────

    private async Task<List<SearchDocument>> BuildSearchDocumentsAsync(
        List<WorkItemDetail> workItems,
        CancellationToken ct)
    {
        var allDocuments = new List<SearchDocument>();
        var semaphore = new SemaphoreSlim(MaxParallelEmbeddings, MaxParallelEmbeddings);

        // Równoległe generowanie embeddingów z throttlingiem
        var tasks = workItems.Select(async wi =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await BuildChunkedDocumentsAsync(wi, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        foreach (var docs in results)
            allDocuments.AddRange(docs);

        return allDocuments;
    }

    /// <summary>
    /// Jeden work item może generować wiele dokumentów gdy tekst przekracza MaxChunkChars.
    /// Chunk 0 ma ID = "{workItemId}", kolejne = "{workItemId}-{n}".
    /// </summary>
    private async Task<List<SearchDocument>> BuildChunkedDocumentsAsync(
        WorkItemDetail wi,
        CancellationToken ct)
    {
        var header = BuildHeaderText(wi);
        var body = BuildBodyText(wi);
        var fullText = string.IsNullOrWhiteSpace(body)
            ? header
            : $"{header}\n\n{body}";

        var chunks = SplitIntoChunks(fullText, MaxChunkChars);
        var documents = new List<SearchDocument>(chunks.Count);

        for (var n = 0; n < chunks.Count; n++)
        {
            ct.ThrowIfCancellationRequested();

            var docId = n == 0 ? wi.Id.ToString() : $"{wi.Id}-{n}";
            var vector = await _embedding.GetEmbeddingAsync(chunks[n], ct);

            // Pola tekstowe (description, AC) tylko w primary chunk — dla search result display
            documents.Add(BuildSearchDocument(wi, docId, isPrimaryChunk: n == 0, vector));
        }

        _logger.LogDebug("[INDEXER] WI#{Id} → {Count} chunk(s).", wi.Id, chunks.Count);

        return documents;
    }

    // ── Azure Search Upload ──────────────────────────────────────────────────

    private async Task UploadToSearchAsync(List<SearchDocument> documents, CancellationToken ct)
    {
        for (var i = 0; i < documents.Count; i += SearchUploadBatchSize)
        {
            var batch = documents.Skip(i).Take(SearchUploadBatchSize).ToList();
            var indexBatch = IndexDocumentsBatch.MergeOrUpload(batch);

            await _searchClient.IndexDocumentsAsync(
                indexBatch,
                new IndexDocumentsOptions { ThrowOnAnyError = true },
                ct);

            _logger.LogInformation("[INDEXER] Uploaded {N} document(s) to '{Index}'.",
                batch.Count, WorkItemsIndex);
        }
    }

    // ── Document Mapping ─────────────────────────────────────────────────────

    private static SearchDocument BuildSearchDocument(
        WorkItemDetail wi,
        string docId,
        bool isPrimaryChunk,
        float[] vector)
    {
        var doc = new SearchDocument
        {
            ["id"] = docId,
            ["title"] = wi.Title ?? string.Empty,
            ["type"] = wi.Type ?? string.Empty,
            ["state"] = wi.State ?? string.Empty,
            ["areaPath"] = wi.AreaPath ?? string.Empty,
            ["tags"] = wi.Tags ?? string.Empty,
            ["url"] = wi.Url ?? string.Empty,
            ["contentVector"] = vector
        };

        // Pola z długą treścią — tylko w primary chunk (chunk 0)
        if (isPrimaryChunk)
        {
            doc["description"] = StripHtml(wi.Description) ?? string.Empty;
            doc["acceptanceCriteria"] = StripHtml(wi.AcceptanceCriteria) ?? string.Empty;
        }

        if (wi.ChangedDate.HasValue)
            doc["changedDate"] = new DateTimeOffset(wi.ChangedDate.Value, TimeSpan.Zero);

        return doc;
    }

    // ── WIQL Query Builder ───────────────────────────────────────────────────

    private static string BuildWiqlQuery(DateTime? since)
    {
        var types = string.Join(", ", IndexableTypes.Select(t => $"'{t}'"));

        var sinceClause = since.HasValue
            ? $"AND [System.ChangedDate] >= '{since.Value:yyyy-MM-ddTHH:mm:ssZ}'"
            : string.Empty;

        return $"""
            SELECT [System.Id]
            FROM WorkItems
            WHERE [System.TeamProject] = @project
              AND [System.WorkItemType] IN ({types})
              AND [System.State] <> 'Removed'
              {sinceClause}
            ORDER BY [System.ChangedDate] DESC
            """;
    }

    // ── Text Processing ──────────────────────────────────────────────────────

    /// <summary>Krótki header z metadanymi — dołączany do każdego chunka jako kontekst.</summary>
    private static string BuildHeaderText(WorkItemDetail wi)
    {
        var sb = new StringBuilder();
        AppendField(sb, "Title", wi.Title);
        AppendField(sb, "Type", wi.Type);
        AppendField(sb, "State", wi.State);
        AppendField(sb, "Area", wi.AreaPath);
        AppendField(sb, "Iteration", wi.IterationPath);
        AppendField(sb, "Assigned To", wi.AssignedTo);
        AppendField(sb, "Tags", wi.Tags);

        if (wi.Relations.Count > 0)
        {
            var related = string.Join(", ", wi.Relations
                .Where(r => r.RelatedId.HasValue)
                .Select(r => $"#{r.RelatedId} ({r.RelationType})"));
            AppendField(sb, "Related Items", related);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Długa treść — description + acceptance criteria (HTML stripped).</summary>
    private static string BuildBodyText(WorkItemDetail wi)
    {
        var sb = new StringBuilder();

        var description = StripHtml(wi.Description);
        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.AppendLine("Description:");
            sb.AppendLine(description);
        }

        var ac = StripHtml(wi.AcceptanceCriteria);
        if (!string.IsNullOrWhiteSpace(ac))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("Acceptance Criteria:");
            sb.Append(ac);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Dzieli tekst na chunki nie przekraczające <paramref name="maxChars"/> znaków,
    /// zachowując granice słów.
    /// </summary>
    private static List<string> SplitIntoChunks(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return [text];

        var chunks = new List<string>();
        var span = text.AsSpan();

        while (span.Length > 0)
        {
            if (span.Length <= maxChars)
            {
                chunks.Add(span.ToString());
                break;
            }

            // Szukamy granicy słowa — nie tniemy w środku wyrazu
            var slice = span[..maxChars];
            var lastSpace = slice.LastIndexOf(' ');
            var cutAt = lastSpace > 0 ? lastSpace : maxChars;

            chunks.Add(span[..cutAt].ToString());
            span = span[cutAt..].TrimStart();
        }

        return chunks;
    }

    private static void AppendField(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"{label}: {value}");
    }

    /// <summary>Usuwa HTML z pól Description i AcceptanceCriteria (DevOps REST API zwraca HTML).</summary>
    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        // Blokowe tagi → spacja
        var text = Regex.Replace(html,
            @"<(br|p|div|li|h\d|tr|td)[^>]*>",
            " ", RegexOptions.IgnoreCase);

        // Usuń pozostałe tagi HTML
        text = Regex.Replace(text, "<[^>]+>", string.Empty);

        // Dekoduj HTML entities (&amp; &lt; itp.)
        text = WebUtility.HtmlDecode(text);

        // Normalizuj białe znaki
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}