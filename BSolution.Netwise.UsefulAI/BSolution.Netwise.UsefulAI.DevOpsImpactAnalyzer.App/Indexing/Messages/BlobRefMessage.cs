namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;

/// <summary>
/// Cienka wiadomość Service Bus zawierająca wyłącznie URI bloba z właściwym payloadem
/// (Claim-Check Pattern). Pozwala ominąć limit 256 KB na wiadomość Service Bus Standard.
///
/// Pełny payload (np. <see cref="WorkItemDetailMessage"/>, <see cref="WikiIndexDocument"/>)
/// jest serializowany do JSON i zapisywany w Blob Storage (container <c>messages</c>).
/// Konsument pobiera blob po URI i deserializuje do właściwego typu.
/// </summary>
public class BlobRefMessage
{
    public string BlobUri { get; set; } = string.Empty;
}
