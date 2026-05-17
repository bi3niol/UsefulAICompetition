using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.Core.Services;

/// <summary>
/// Pobiera listę ID work itemów do (re)przetwarzania przez zapytanie WIQL.
/// Obsługuje tryb pełny (since == null) i przyrostowy (since != null).
/// </summary>
public interface IWorkItemQueryService
{
    Task<List<int>> QueryIdsAsync(DateTime? since, CancellationToken ct = default);
}

/// <summary>
/// Generyczna implementacja <see cref="IWorkItemQueryService"/> sparametryzowana
/// listą typów work itemów do filtrowania. Każda aplikacja rejestruje własną
/// instancję — np. Impact Analyzer chce wszystkie typy łącznie z Tasks/Bugs,
/// a Wiki Doc Generator tylko Feature/Epic/User Story/PBI.
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

        // WIQL używa precyzji dziennej dla pól daty — nie wolno podawać godziny.
        // Odejmujemy 1 dzień, by nie zgubić elementów zmienionych tego samego dnia
        // co poprzednia synchronizacja (drobne duplikaty są filtrowane przez konsumentów,
        // np. Impact Analyzer MergeOrUpload, Wiki Doc Generator ETag-i).
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
