using System.Text.RegularExpressions;

namespace BSolution.Netwise.UsefulAI.Core.Stores;

/// <summary>
/// Helper elements for building blob paths in the style
/// <c>{subfolder}/{yyyy-MM-dd}/{identity}_{guid8}.json</c> used by
/// claim-check pipelines. Concrete paths are defined by each application
/// project in its own BlobPaths class, using these helpers.
/// </summary>
public static class BlobPathHelpers
{
    /// <summary>Current UTC date in <c>yyyy-MM-dd</c> format.</summary>
    public static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");

    /// <summary>8-character uniqueness identifier (hex) for a single blob.</summary>
    public static string Uid => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Sanitizes text into a safe blob name segment: <c>/</c> and <c>_</c> → <c>-</c>,
    /// only <c>[a-zA-Z0-9-]</c>, max 50 characters. Returns "x" for empty results.
    /// </summary>
    public static string Slug(string s) =>
        Regex.Replace(s.Replace('/', '-').Replace('_', '-').TrimStart('-'), @"[^a-zA-Z0-9\-]", "")
             is { Length: > 0 } slug
             ? slug[..Math.Min(slug.Length, 50)]
             : "x";
}
