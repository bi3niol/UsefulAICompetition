using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;

/// <summary>
/// Wiadomość Service Bus z pełną treścią pobranej strony WIKI
/// (kolejka <c>wiki-pages</c>).
/// </summary>
public class WikiPageContentMessage
{
    public string WikiId { get; set; } = string.Empty;
    public string? WikiName { get; set; }
    public WikiPageDetail Page { get; set; } = new();
}
