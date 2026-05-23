using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Services;

/// <summary>
/// Stosuje filtry z <see cref="CodeScanOptions"/> do plików zwracanych przez DevOps API.
/// Reguły:
///   - tylko pliki (foldery odpadają),
///   - rozszerzenie musi być na białej liście,
///   - żaden segment ścieżki nie może być na czarnej liście katalogów,
///   - rozmiar pliku &lt;= <see cref="CodeScanOptions.MaxFileBytes"/> (gdy znany).
/// </summary>
public static class CodeFileFilter
{
    public static bool IsIncluded(string path, long? sizeBytes, CodeScanOptions options)
    {
        if (string.IsNullOrEmpty(path)) return false;

        // Foldery / pliki bez rozszerzenia ignorujemy.
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;

        if (!options.IncludeExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
            return false;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (options.ExcludeFolders.Any(f => string.Equals(f, segment, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (sizeBytes is long size && size > options.MaxFileBytes)
            return false;

        return true;
    }

    public static IEnumerable<GitItem> FilterTree(IEnumerable<GitItem> items, CodeScanOptions options) =>
        items.Where(i =>
            !i.IsFolder
            && i.Path is not null
            && IsIncluded(i.Path, i.Size, options));
}
