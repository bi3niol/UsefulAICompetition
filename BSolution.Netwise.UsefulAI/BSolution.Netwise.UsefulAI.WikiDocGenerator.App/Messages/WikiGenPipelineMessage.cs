using System.Text.Json.Serialization;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Messages;

/// <summary>
/// Wiadomość Service Bus przekazywana z Stage 1 (timer/webhook) do Stage 2
/// (pipeline). Enkapsuluje oba tryby wejścia — Researcher rozstrzyga na
/// podstawie <see cref="Source"/>, co robi.
/// </summary>
/// <remarks>
/// Wiadomość jest mała (tylko metadane/ID-ki), więc NIE potrzebujemy Claim-Check.
/// Gdyby payload rósł (np. pełna lista plików przy full code scan), wtedy
/// wdrożymy wzorzec BlobRefMessage jak w Impact Analyzerze.
/// </remarks>
public class WikiGenPipelineMessage
{
    /// <summary>
    /// Typ źródła: <c>PullRequest</c>, <c>WorkItems</c> lub <c>CodeScan</c>.
    /// Stage 2 na tej podstawie buduje właściwy seed-prompt.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WikiGenSource Source { get; set; }

    // ── Pull Request fields ────────────────────────────────────────────────

    public string? RepositoryId { get; set; }
    public string? RepositoryName { get; set; }
    public int? PullRequestId { get; set; }
    public string? PrTitle { get; set; }
    public string? PrDescription { get; set; }
    public string? SourceBranch { get; set; }
    public string? TargetBranch { get; set; }
    public string? MergeCommitId { get; set; }
    public string? Author { get; set; }
    public List<int> LinkedWorkItemIds { get; set; } = [];

    // ── Work Items fields ──────────────────────────────────────────────────

    public List<int> WorkItemIds { get; set; } = [];
}

public enum WikiGenSource
{
    PullRequest,
    WorkItems,
    CodeScan
}
