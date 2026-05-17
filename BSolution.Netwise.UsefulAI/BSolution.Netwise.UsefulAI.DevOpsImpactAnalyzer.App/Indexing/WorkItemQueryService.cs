using BSolution.Netwise.UsefulAI.Core.Services;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

/// <summary>
/// Serwis odpowiedzialny wyłącznie za pobieranie listy ID work itemów do (re)indeksacji
/// poprzez zapytanie WIQL — pełna lub przyrostowa synchronizacja.
/// </summary>
public interface IWorkItemQueryService
{
    Task<List<int>> QueryIdsAsync(DateTime? since, CancellationToken ct = default);
}

public class WorkItemQueryService : IWorkItemQueryService
{
    private readonly IAzureDevOpsService _devOps;
    private readonly ILogger<WorkItemQueryService> _logger;

    private static readonly string[] IndexableTypes =
    [
        "User Story", "Product Backlog Item", "Bug",
        "Task", "Epic", "Feature", "Requirement"
    ];

    public WorkItemQueryService(IAzureDevOpsService devOps, ILogger<WorkItemQueryService> logger)
    {
        _devOps = devOps;
        _logger = logger;
    }

    public async Task<List<int>> QueryIdsAsync(DateTime? since, CancellationToken ct = default)
    {
        var wiql = BuildWiqlQuery(since);
        var ids = await _devOps.QueryWorkItemIdsAsync(wiql, ct);

        _logger.LogInformation(
            "[WI-QUERY] WIQL ({Mode}) returned {Count} work item id(s).",
            since.HasValue ? "incremental" : "full",
            ids.Count);

        return ids;
    }

    private static string BuildWiqlQuery(DateTime? since)
    {
        var types = string.Join(", ", IndexableTypes.Select(t => $"'{t}'"));

        // WIQL używa precyzji dziennej dla pól daty — nie wolno podawać godziny.
        // Odejmujemy 1 dzień, by nie zgubić elementów zmienionych tego samego dnia
        // co poprzednia synchronizacja (drobne duplikaty są filtrowane przez MergeOrUpload).
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
