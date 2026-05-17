using BSolution.Netwise.UsefulAI.Core.Stores;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Stores;

/// <summary>
/// Generuje ścieżki blobów wg konwencji:
/// <c>{subfolder}/{yyyy-MM-dd}/{identity}_{guid8}.json</c>
///
/// Subfoldery odpowiadają nazwom kolejek Service Bus do których trafia dana wiadomość.
/// Guid8 (8 hex) = unikatowość przy wielokrotnych re-indeksowaniach tej samej treści.
/// </summary>
public static class BlobPaths
{
    /// <summary>Blob dla <c>WorkItemDetailMessage</c> na kolejce <c>workitem-details</c>.</summary>
    public static string WorkItemDetail(int wiId) =>
        $"workitem-details/{BlobPathHelpers.Today}/{wiId}_{BlobPathHelpers.Uid}.json";

    /// <summary>Blob dla listy wszystkich chunków <c>WorkItemIndexDocument</c> jednego WI na kolejce <c>workitem-documents</c>.</summary>
    public static string WorkItemDocument(int wiId) =>
        $"workitem-documents/{BlobPathHelpers.Today}/{wiId}_{BlobPathHelpers.Uid}.json";

    /// <summary>Blob dla <c>WikiPageContentMessage</c> na kolejce <c>wiki-pages</c>.</summary>
    public static string WikiPage(string wikiId, string path) =>
        $"wiki-pages/{BlobPathHelpers.Today}/{BlobPathHelpers.Slug(wikiId)}-{BlobPathHelpers.Slug(path)}_{BlobPathHelpers.Uid}.json";

    /// <summary>Blob dla listy wszystkich chunków <c>WikiIndexDocument</c> jednej strony na kolejce <c>wiki-documents</c>.</summary>
    public static string WikiDocument(string wikiId, string path) =>
        $"wiki-documents/{BlobPathHelpers.Today}/{BlobPathHelpers.Slug(wikiId)}-{BlobPathHelpers.Slug(path)}_{BlobPathHelpers.Uid}.json";

    /// <summary>Deterministyczna nazwa bloba raportu Impact Analysis (markdown) wewnątrz kontenera <c>reports</c>.</summary>
    public static string Report(int workItemId) =>
        $"{workItemId}.md";
}
