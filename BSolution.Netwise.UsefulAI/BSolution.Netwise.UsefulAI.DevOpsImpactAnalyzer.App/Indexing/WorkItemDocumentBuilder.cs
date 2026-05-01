using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

/// <summary>
/// Serwis budujący listę dokumentów Azure Search (chunked, z wektorami) na podstawie
/// pojedynczego <see cref="WorkItemDetail"/>. Wykonuje wywołania Embedding API.
/// </summary>
public interface IWorkItemDocumentBuilder
{
    Task<List<WorkItemIndexDocument>> BuildAsync(WorkItemDetail wi, CancellationToken ct = default);
}

public class WorkItemDocumentBuilder : IWorkItemDocumentBuilder
{
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<WorkItemDocumentBuilder> _logger;

    private const int MaxChunkChars = 6_000;

    public WorkItemDocumentBuilder(
        IEmbeddingService embedding,
        ILogger<WorkItemDocumentBuilder> logger)
    {
        _embedding = embedding;
        _logger = logger;
    }

    public async Task<List<WorkItemIndexDocument>> BuildAsync(
        WorkItemDetail wi,
        CancellationToken ct = default)
    {
        var header = BuildHeaderText(wi);
        var body = BuildBodyText(wi);
        var fullText = string.IsNullOrWhiteSpace(body) ? header : $"{header}\n\n{body}";

        var chunks = fullText.SplitIntoChunks(MaxChunkChars, overlapFraction: 0.2);
        var documents = new List<WorkItemIndexDocument>(chunks.Count);

        var vectors = await _embedding.GetEmbeddingsAsync(chunks, ct);

        for (var n = 0; n < chunks.Count; n++)
        {
            ct.ThrowIfCancellationRequested();
            var docId = n == 0 ? wi.Id.ToString() : $"{wi.Id}-{n}";
            documents.Add(BuildDocument(wi, docId, isPrimaryChunk: n == 0, vectors[n]));
        }

        _logger.LogInformation("[WI-BUILD] WI#{Id} → {Count} document chunk(s).",
            wi.Id, documents.Count);

        return documents;
    }

    // ── Document Mapping ─────────────────────────────────────────────────────

    private static WorkItemIndexDocument BuildDocument(
        WorkItemDetail wi,
        string docId,
        bool isPrimaryChunk,
        float[] vector)
    {
        var doc = new WorkItemIndexDocument
        {
            Id = docId,
            Title = wi.Title ?? string.Empty,
            Type = wi.Type ?? string.Empty,
            State = wi.State ?? string.Empty,
            AreaPath = wi.AreaPath ?? string.Empty,
            Tags = wi.Tags ?? string.Empty,
            Url = wi.Url ?? string.Empty,
            ContentVector = vector
        };

        // Pola z długą treścią — tylko w primary chunk (chunk 0)
        if (isPrimaryChunk)
        {
            doc.Description = StripHtml(wi.Description) ?? string.Empty;
            doc.AcceptanceCriteria = StripHtml(wi.AcceptanceCriteria) ?? string.Empty;
            doc.Comments = FormatComments(wi.Comments) ?? string.Empty;
        }

        if (wi.ChangedDate.HasValue)
            doc.ChangedDate = new DateTimeOffset(wi.ChangedDate.Value, TimeSpan.Zero);

        return doc;
    }

    // ── Text Processing ──────────────────────────────────────────────────────

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

        var comments = FormatComments(wi.Comments);
        if (!string.IsNullOrWhiteSpace(comments))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Comments:");
            sb.Append(comments);
        }

        return sb.ToString().TrimEnd();
    }

    private static string? FormatComments(IReadOnlyList<WorkItemComment>? comments)
    {
        if (comments is null || comments.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var c in comments)
        {
            var text = StripHtml(c.Text);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var date = c.CreatedDate?.ToString("yyyy-MM-dd") ?? "unknown";
            var author = string.IsNullOrWhiteSpace(c.CreatedBy) ? "unknown" : c.CreatedBy;
            sb.Append('[').Append(date).Append(" by ").Append(author).Append("]: ");
            sb.AppendLine(text);
        }

        return sb.Length == 0 ? null : sb.ToString().TrimEnd();
    }

    private static void AppendField(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"{label}: {value}");
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var text = Regex.Replace(html,
            @"<(br|p|div|li|h\d|tr|td)[^>]*>",
            " ", RegexOptions.IgnoreCase);

        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);

        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
