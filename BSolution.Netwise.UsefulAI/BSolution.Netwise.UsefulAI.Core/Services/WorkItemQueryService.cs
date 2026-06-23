using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.Core.Services;

/// <summary>
/// Fetches the list of work item IDs for (re)processing via a WIQL query.
/// Supports full mode (since == null) and incremental mode (since != null).
/// </summary>
public interface IWorkItemQueryService
{
    Task<List<int>> QueryIdsAsync(DateTime? since, CancellationToken ct = default);
}

/// <summary>
/// Generic implementation of <see cref="IWorkItemQueryService"/> parameterized by
/// a list of work item types to filter by. Each application registers its own
/// instance — e.g. Impact Analyzer wants all types including Tasks/Bugs,
/// while Wiki Doc Generator only wants Feature/Epic/User Story/PBI.
/// </summary>
public class WorkItemQueryService : IWorkItemQueryService
{
    private readonly IAzureDevOpsService _devOps;
    private readonly ILogger<WorkItemQueryService> _logger;
    private readonly string _logTag;
    private readonly IReadOnlyList<string> _workItemTypes;

    public WorkItemQueryService(
        IAzureDevOpsService devOps,
        ILogger<WorkItemQueryService> logger,
        IReadOnlyList<string> workItemTypes,
        string logTag = "WI-QUERY")
    {
        if (workItemTypes is null || workItemTypes.Count == 0)
            throw new ArgumentException("At least one work item type must be provided.", nameof(workItemTypes));

        _devOps = devOps;
        _logger = logger;
        _workItemTypes = workItemTypes;
        _logTag = logTag;
    }

    public async Task<List<int>> QueryIdsAsync(DateTime? since, CancellationToken ct = default)
    {
        var wiql = BuildWiqlQuery(since);
        var ids = await _devOps.QueryWorkItemIdsAsync(wiql, ct);

        _logger.LogInformation(
            "[{Tag}] WIQL ({Mode}) returned {Count} work item id(s).",
            _logTag,
            since.HasValue ? "incremental" : "full",
            ids.Count);

        return ids;
    }

    private string BuildWiqlQuery(DateTime? since)
    {
        var types = string.Join(", ", _workItemTypes.Select(t => $"'{t}'"));

        // WIQL uses day-level precision for date fields — time component must not be included.
        // We subtract 1 day to avoid missing items changed on the same day as the previous
        // synchronization (minor duplicates are filtered by consumers,
        // e.g. Impact Analyzer MergeOrUpload, Wiki Doc Generator ETags).
        var sinceClause = since.HasValue
            ? $"AND [System.ChangedDate] >= '{since.Value.Date.AddDays(-1):yyyy-MM-dd}'"
            : string.Empty;

        return $"""
            SELECT [System.Id]
            FROM WorkItems
            WHERE [System.TeamProject] = @project
              AND [System.WorkItemType] IN ({types})
              AND [System.State] <> 'Removed'
              {sinceClause}
            ORDER BY [System.ChangedDate] DESC
            """;
    }
}
