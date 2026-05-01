using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

/// <summary>
/// Serwis budujący listę dokumentów Azure Search dla pojedynczej strony WIKI.
/// Wykonuje chunkowanie Markdown po nagłówkach H1/H2 oraz wywołania Embedding API.
/// </summary>
public interface IWikiDocumentBuilder
{
    Task<List<WikiIndexDocument>> BuildAsync(
        string wikiId,
        WikiPageDetail page,
        CancellationToken ct = default);
}

public class WikiDocumentBuilder : IWikiDocumentBuilder
{
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<WikiDocumentBuilder> _logger;

    private const int MaxChunkChars = 4_500;

    public WikiDocumentBuilder(
        IEmbeddingService embedding,
        ILogger<WikiDocumentBuilder> logger)
    {
        _embedding = embedding;
        _logger = logger;
    }

    public async Task<List<WikiIndexDocument>> BuildAsync(
        string wikiId,
        WikiPageDetail page,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(page.Content) || string.IsNullOrWhiteSpace(page.Path))
            return [];

        var title = ExtractTitle(page.Path!, page.Content!);
        var chunks = ChunkMarkdown(page.Content!, MaxChunkChars);

        var documents = new List<WikiIndexDocument>(chunks.Count);

        var chunkTexts = chunks.Select(c => $"Page: {title}\n\n{c}").ToList();
        var vectors = await _embedding.GetEmbeddingsAsync(chunkTexts, ct);

        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var docId = BuildDocId(wikiId, page.Path!, i);
            var chunkText = chunkTexts[i];
            var excerpt = chunkText.Length > 500 ? chunkText[..500] + "…" : chunkText;

            documents.Add(new WikiIndexDocument
            {
                Id = docId,
                Title = title,
                Path = page.Path!,
                WikiId = wikiId,
                Content = chunkText,
                ContentExcerpt = excerpt,
                Url = page.RemoteUrl ?? string.Empty,
                ContentVector = vectors[i]
            });
        }

        _logger.LogInformation("[WIKI-BUILD] Page '{Path}' → {Count} document chunk(s).",
            page.Path, documents.Count);

        return documents;
    }

    // ── Markdown Chunking ────────────────────────────────────────────────────

    /// <summary>
    /// Dzieli tekst Markdown na chunki po granicach nagłówków H1/H2.
    /// Małe sekcje są łączone; duże sekcje są dalej dzielone po granicy słowa.
    /// </summary>
    private static List<string> ChunkMarkdown(string content, int maxChars)
    {
        if (content.Length <= maxChars) return [content];

        var headingRegex = new Regex(@"(?m)^#{1,2} .+$", RegexOptions.Multiline);
        var matches = headingRegex.Matches(content);

        if (matches.Count == 0)
            return content.SplitIntoChunks(maxChars, overlapFraction: 0.2);

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

        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var section in sections)
        {
            if (section.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString().Trim());
                    current.Clear();
                }

                chunks.AddRange(section.SplitIntoChunks(maxChars, overlapFraction: 0.2));
                continue;
            }

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

    // ── ID + Title Helpers ───────────────────────────────────────────────────

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
