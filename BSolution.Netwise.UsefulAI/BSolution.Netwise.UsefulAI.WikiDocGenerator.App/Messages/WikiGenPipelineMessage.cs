using System.Text.Json.Serialization;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Messages;

/// <summary>
/// Wiadomosc Service Bus przekazywana z Stage 1 (timer/webhook) do Stage 2
/// (pipeline). Enkapsuluje oba tryby wejscia - Researcher rozstrzyga na
/// podstawie <see cref="Source"/>, co robi.
/// </summary>
public class WikiGenPipelineMessage
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WikiGenSource Source { get; set; }

    // Pull Request fields
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

    // Work Items fields
    public List<int> WorkItemIds { get; set; } = [];
}

public enum WikiGenSource
{
    PullRequest,
    WorkItems,
    CodeScan
}
