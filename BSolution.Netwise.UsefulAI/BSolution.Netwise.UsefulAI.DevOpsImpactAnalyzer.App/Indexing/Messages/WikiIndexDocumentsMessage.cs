namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;

/// <summary>
/// Wiadomość Service Bus z listą gotowych do uploadu dokumentów Azure Search
/// dla pojedynczej strony WIKI (kolejka <c>wiki-documents</c>).
/// </summary>
public class WikiIndexDocumentsMessage
{
    public List<WikiIndexDocument> Documents { get; set; } = [];
}
