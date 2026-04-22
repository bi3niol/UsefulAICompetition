namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

/// <summary>
/// Reprezentacja jednego dokumentu indeksu <c>work-items-index</c>, używana
/// jako payload przekazywany przez Service Bus pomiędzy etapami indeksacji.
///
/// W odróżnieniu od <see cref="Azure.Search.Documents.Models.SearchDocument"/>
/// (która jest zwykłym <c>Dictionary&lt;string, object&gt;</c>) ten typ ma
/// jednoznaczny schemat, co gwarantuje deterministyczną deserializację
/// (m.in. <c>float[]</c> dla wektora i <c>DateTimeOffset</c> dla daty).
/// </summary>
public class WorkItemIndexDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string AreaPath { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>Wektor 3072 wymiarów (text-embedding-3-large).</summary>
    public float[] ContentVector { get; set; } = [];

    /// <summary>Wypełnione tylko dla primary chunk (chunk 0).</summary>
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string? Comments { get; set; }

    public DateTimeOffset? ChangedDate { get; set; }
}
