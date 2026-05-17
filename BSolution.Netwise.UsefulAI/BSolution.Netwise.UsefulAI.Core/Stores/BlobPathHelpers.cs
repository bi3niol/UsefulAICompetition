using System.Text.RegularExpressions;

namespace BSolution.Netwise.UsefulAI.Core.Stores;

/// <summary>
/// Pomocnicze elementy do budowania ścieżek blobów w stylu
/// <c>{subfolder}/{yyyy-MM-dd}/{identity}_{guid8}.json</c> używanym przez
/// claim-check pipeline'y. Konkretne ścieżki definiuje każdy projekt
/// aplikacyjny we własnej klasie BlobPaths, korzystając z tych helperów.
/// </summary>
public static class BlobPathHelpers
{
    /// <summary>Bieżąca data UTC w formacie <c>yyyy-MM-dd</c>.</summary>
    public static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");

    /// <summary>8-znakowy identyfikator unikalności (hex) dla pojedynczego bloba.</summary>
    public static string Uid => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Sanityzuje tekst do bezpiecznej formy w nazwie bloba: <c>/</c> i <c>_</c> → <c>-</c>,
    /// tylko <c>[a-zA-Z0-9-]</c>, max 50 znaków. Pusty wynik zwraca "x".
    /// </summary>
    public static string Slug(string s) =>
        Regex.Replace(s.Replace('/', '-').Replace('_', '-').TrimStart('-'), @"[^a-zA-Z0-9\-]", "")
             is { Length: > 0 } slug
             ? slug[..Math.Min(slug.Length, 50)]
             : "x";
}
