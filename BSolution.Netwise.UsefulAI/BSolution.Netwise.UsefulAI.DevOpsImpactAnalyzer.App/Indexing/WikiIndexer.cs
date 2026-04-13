using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

public interface IWikiIndexer
{
    /// <summary>Synchronizuje wszystkie wiki z Azure AI Search.</summary>
    Task RunSyncAsync(CancellationToken ct = default);
}

public class WikiIndexer : IWikiIndexer
{
    private readonly IAzureDevOpsService _devOps;
    private readonly IEmbeddingService _embedding;
    private readonly SearchClient _searchClient;
    private readonly ILogger<WikiIndexer> _logger;

    private const string WikiIndex = "wiki-pages-index";

    // ~1500 tokenów (średnio 4 znaki/token)
    private const int MaxChunkChars = 6_000;

    // Throttling wywołań Embedding API
    private const int MaxParallelEmbeddings = 4;

    // Batch upload do Azure Search
    private const int SearchUploadBatchSize = 500;

    public WikiIndexer(
        IAzureDevOpsService devOps,
        IEmbeddingService embedding,
        IConfiguration config,
        ILogger<WikiIndexer> logger)
    {
        _devOps = devOps;
        _embedding = embedding;
        _logger = logger;

        _searchClient = new SearchClient(
            new Uri(config["AzureSearch:Endpoint"]!),
            WikiIndex,
            new AzureKeyCredential(config["AzureSearch:ApiKey"]!));
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public async Task RunSyncAsync(CancellationToken ct = default)
    {
        var wikis = await _devOps.GetWikiListAsync(ct);
        _logger.LogInformation("[WIKI-INDEXER] Found {Count} wiki(s) in project.", wikis.Count);

        foreach (var wiki in wikis)
        {
            if (wiki.Id is null) continue;

            try
            {
                await SyncWikiAsync(wiki, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WIKI-INDEXER] Failed to sync wiki '{Name}' ({Id}).",
                    wiki.Name, wiki.Id);
            }
        }
    }

    // ── Core Sync ────────────────────────────────────────────────────────────

    private async Task SyncWikiAsync(WikiInfo wiki, CancellationToken ct)
    {
        _logger.LogInformation("[WIKI-INDEXER] Syncing wiki '{Name}' ({Id})...",
            wiki.Name, wiki.Id);

        // Krok 1: pobierz wszystkie ścieżki stron (bez treści — jedno szybkie żądanie)
        var paths = await _devOps.GetWikiPagePathsAsync(wiki.Id!, ct);
        _logger.LogInformation("[WIKI-INDEXER] Found {Count} page path(s) in '{Name}'.",
            paths.Count, wiki.Name);

        if (paths.Count == 0) return;

        // Krok 2: zrównoleglone pobieranie treści + generowanie embeddingów
        var semaphore = new SemaphoreSlim(MaxParallelEmbeddings, MaxParallelEmbeddings);

        var tasks = paths.Select(async path =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await BuildPageDocumentsAsync(wiki, path, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[WIKI-INDEXER] Skipping page '{Path}' — {Message}", path, ex.Message);
                return [];
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        var allDocuments = results.SelectMany(docs => docs).ToList();

        // Krok 3: upload do Azure Search
        await UploadToSearchAsync(allDocuments, ct);

        _logger.LogInformation(
            "[WIKI-INDEXER] Wiki '{Name}' sync complete — {DocCount} document(s) from {PageCount} page(s).",
            wiki.Name, allDocuments.Count, paths.Count);
    }

    // ── Page → Documents ─────────────────────────────────────────────────────

    private async Task<List<SearchDocument>> BuildPageDocumentsAsync(
        WikiInfo wiki, string path, CancellationToken ct)
    {
        var page = await _devOps.GetWikiPageAsync(wiki.Id!, path, ct);

        if (string.IsNullOrWhiteSpace(page.Content))
            return [];

        var title = ExtractTitle(path, page.Content);
        var chunks = ChunkMarkdown(page.Content, MaxChunkChars);

        var documents = new List<SearchDocument>(chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var docId = BuildDocId(wiki.Id!, path, i);
            var chunkText = $"Page: {title}\n\n{chunks[i]}";
            var vector = await _embedding.GetEmbeddingAsync(chunkText, ct);
            var excerpt = chunkText.Length > 500 ? chunkText[..500] + "…" : chunkText;

            documents.Add(new SearchDocument
            {
                ["id"] = docId,
                ["title"] = title,
                ["path"] = path,
                ["wikiId"] = wiki.Id,
                ["content"] = chunkText,
                ["contentExcerpt"] = excerpt,
                ["url"] = page.RemoteUrl ?? string.Empty,
                ["contentVector"] = vector
            });
        }

        _logger.LogDebug("[WIKI-INDEXER] Page '{Path}' → {Count} chunk(s).", path, chunks.Count);

        return documents;
    }

    // ── Upload ───────────────────────────────────────────────────────────────

    private async Task UploadToSearchAsync(List<SearchDocument> documents, CancellationToken ct)
    {
        if (documents.Count == 0) return;

        for (var i = 0; i < documents.Count; i += SearchUploadBatchSize)
        {
            var batch = documents.Skip(i).Take(SearchUploadBatchSize).ToList();

            await _searchClient.IndexDocumentsAsync(
                IndexDocumentsBatch.MergeOrUpload(batch),
                new IndexDocumentsOptions { ThrowOnAnyError = true },
                ct);

            _logger.LogInformation("[WIKI-INDEXER] Uploaded {N} document(s) to '{Index}'.",
                batch.Count, WikiIndex);
        }
    }

    // ── Markdown Chunking ────────────────────────────────────────────────────

    /// <summary>
    /// Dzieli tekst Markdown na chunki po granicach nagłówków H1/H2.
    /// Małe sekcje są łączone; duże sekcje są dalej dzielone po granicy słowa.
    /// </summary>
    private static List<string> ChunkMarkdown(string content, int maxChars)
    {
        if (content.Length <= maxChars)
            return [content];

        // Znajdź wszystkie nagłówki H1/H2 na początku linii
        var headingRegex = new Regex(@"(?m)^#{1,2} .+$", RegexOptions.Multiline);
        var matches = headingRegex.Matches(content);

        if (matches.Count == 0)
            return SplitByWordBoundary(content, maxChars);

        // Wytnij sekcje: [treść przed 1. nagłówkiem] + [nagłówek + treść do następnego]
        var sections = new List<string>();

        if (matches[0].Index > 0)
        {
            var preamble = content[..matches[0].Index].Trim();
            if (!string.IsNullOrWhiteSpace(preamble))
                sections.Add(preamble);
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            var section = content[start..end].Trim();

            if (!string.IsNullOrWhiteSpace(section))
                sections.Add(section);
        }

        // Łącz małe sekcje w chunki; duże sekcje tniemy
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var section in sections)
        {
            if (section.Length > maxChars)
            {
                // Flush bieżącego chunka
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString().Trim());
                    current.Clear();
                }

                chunks.AddRange(SplitByWordBoundary(section, maxChars));
                continue;
            }

            // Czy sekcja zmieści się w bieżącym chunku?
            var separator = current.Length > 0 ? "\n\n" : string.Empty;

            if (current.Length + separator.Length + section.Length > maxChars && current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }

            if (current.Length > 0)
                current.Append("\n\n");

            current.Append(section);
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        return chunks.Count > 0 ? chunks : [content];
    }

    /// <summary>Dzieli długi tekst na kawałki respektując granice słów.</summary>
    private static List<string> SplitByWordBoundary(string text, int maxChars)
    {
        if (text.Length <= maxChars) return [text];

        var chunks = new List<string>();
        var span = text.AsSpan();

        while (span.Length > 0)
        {
            if (span.Length <= maxChars)
            {
                chunks.Add(span.ToString());
                break;
            }

            var slice = span[..maxChars];
            var lastSpace = slice.LastIndexOf(' ');
            var cutAt = lastSpace > 0 ? lastSpace : maxChars;

            chunks.Add(span[..cutAt].ToString());
            span = span[cutAt..].TrimStart();
        }

        return chunks;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Stabilne ID dokumentu oparte na MD5 z "wikiId:path".
    /// Chunk 0 = sam hash; chunk N = hash-N.
    /// </summary>
    private static string BuildDocId(string wikiId, string path, int chunkIndex)
    {
        var hash = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes($"{wikiId}:{path}")));

        return chunkIndex == 0 ? hash : $"{hash}-{chunkIndex}";
    }

    /// <summary>Tytuł z pierwszego H1 w treści lub z ostatniego segmentu ścieżki.</summary>
    private static string ExtractTitle(string path, string content)
    {
        var h1 = Regex.Match(content, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (h1.Success) return h1.Groups[1].Value.Trim();

        return path.Split('/')
            .LastOrDefault(s => !string.IsNullOrWhiteSpace(s))
            ?.Replace('-', ' ')
            .Replace('_', ' ')
            ?? path;
    }
}