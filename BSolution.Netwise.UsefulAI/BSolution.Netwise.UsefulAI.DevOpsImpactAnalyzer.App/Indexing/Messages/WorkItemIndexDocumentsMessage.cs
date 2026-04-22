namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;

/// <summary>
/// Wiadomość Service Bus zawierająca listę gotowych do uploadu dokumentów Azure Search
/// dla pojedynczego work itemu (kolejka <c>workitem-documents</c>).
/// </summary>
public class WorkItemIndexDocumentsMessage
{
    public List<WorkItemIndexDocument> Documents { get; set; } = [];
}
