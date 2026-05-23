using BSolution.Netwise.UsefulAI.Core.Stores;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Stores;

/// <summary>
/// Ścieżki blobów w kontenerze <c>messages</c> używane przez claim-check
/// pipeline WikiDocGeneratora. Kompletne z <see cref="BlobPathHelpers"/>.
/// </summary>
public static class BlobPaths
{
    /// <summary>
    /// Findings z Researchera (Stage 2 → Stage 3).
    /// Format: <c>wikigen/findings/{date}/{slug}_{uid}.json</c>
    /// </summary>
    public static string Findings(string context) =>
        $"wikigen/findings/{BlobPathHelpers.Today}/{BlobPathHelpers.Slug(context)}_{BlobPathHelpers.Uid}.json";

    /// <summary>
    /// Draft z Writer+Editor loop (Stage 3 → Stage 4).
    /// Format: <c>wikigen/drafts/{date}/{slug}_{uid}.json</c>
    /// </summary>
    public static string Draft(string context) =>
        $"wikigen/drafts/{BlobPathHelpers.Today}/{BlobPathHelpers.Slug(context)}_{BlobPathHelpers.Uid}.json";
}
