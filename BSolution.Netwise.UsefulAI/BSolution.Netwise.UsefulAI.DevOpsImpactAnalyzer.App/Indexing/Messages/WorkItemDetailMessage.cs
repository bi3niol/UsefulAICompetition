using BSolution.Netwise.UsefulAI.Core.Models;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;

/// <summary>
/// Wiadomość Service Bus zawierająca pełne dane jednego work itemu wraz z komentarzami
/// (kolejka <c>workitem-details</c>).
/// </summary>
public class WorkItemDetailMessage
{
    public WorkItemDetail WorkItem { get; set; } = new();
}
