namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

/// <summary>
/// Reprezentacja jednego dokumentu indeksu <c>wiki-pages-index</c> używana jako payload
/// przekazywany przez Service Bus pomiędzy etapami indeksacji WIKI.
///
/// Analog do <see cref="WorkItemIndexDocument"/> — typowany odpowiednik
/// <see cref="Azure.Search.Documents.Models.SearchDocument"/>, gwarantujący
/// deterministyczną deserializację (zwłaszcza <c>float[]</c> wektora).
/// </summary>
public class WikiIndexDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string WikiId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ContentExcerpt { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>Wektor 3072 wymiarów (text-embedding-3-large).</summary>
    public float[] ContentVector { get; set; } = [];
}
