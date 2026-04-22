namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;

/// <summary>
/// Wiadomość Service Bus zawierająca paczkę ID work itemów do pobrania
/// (kolejka <c>workitem-ids</c>).
/// </summary>
public class WorkItemIdsBatchMessage
{
    public List<int> Ids { get; set; } = [];
}
