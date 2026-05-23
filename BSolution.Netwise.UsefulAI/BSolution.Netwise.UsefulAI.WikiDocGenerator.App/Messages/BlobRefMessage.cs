namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Messages;

/// <summary>
/// Cienka wiadomość Service Bus zawierająca wyłącznie URI bloba z właściwym payloadem
/// (Claim-Check Pattern). Identyczny wzorzec jak w Impact Analyzerze.
/// Pozwala ominąć limit 256 KB na wiadomość Service Bus Standard.
/// </summary>
public class BlobRefMessage
{
    public string BlobUri { get; set; } = string.Empty;
}
