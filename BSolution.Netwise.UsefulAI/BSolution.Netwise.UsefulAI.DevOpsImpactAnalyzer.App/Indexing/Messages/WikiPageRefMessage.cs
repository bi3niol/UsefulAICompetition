namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;

/// <summary>
/// Wiadomość Service Bus z referencją do pojedynczej strony WIKI do pobrania
/// (kolejka <c>wiki-page-refs</c>). Analog do <see cref="WorkItemIdsBatchMessage"/>,
/// ale dla WIKI pojedyncza wiadomość per strona — DevOps API nie ma batchowego
/// endpointu na strony WIKI, więc paczkowanie nic nie daje.
/// </summary>
public class WikiPageRefMessage
{
    public string WikiId { get; set; } = string.Empty;
    public string? WikiName { get; set; }
    public string Path { get; set; } = string.Empty;
}
