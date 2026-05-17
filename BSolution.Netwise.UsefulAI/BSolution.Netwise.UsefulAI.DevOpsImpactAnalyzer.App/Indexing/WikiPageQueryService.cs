using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using BSolution.Netwise.UsefulAI.Core.Services;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

/// <summary>
/// Serwis odpowiedzialny wyłącznie za enumerację wszystkich stron WIKI w projekcie
/// (lista wiki + płaska lista ścieżek per wiki).
/// </summary>
public interface IWikiPageQueryService
{
    /// <summary>
    /// Zwraca referencje do stron wiki do zaindeksowania.
    /// Gdy <paramref name="since"/> jest podane, zwraca tylko strony zmodyfikowane po tej dacie
    /// (filtrowanie przez Git Commits API). Gdy filtrowanie inkrementalne nie jest możliwe
    /// (brak repozytorium Git pod wiki), wykonuje pełną synchronizację jako fallback.
    /// </summary>
    Task<List<WikiPageRefMessage>> QueryPageRefsAsync(DateTimeOffset? since = null, CancellationToken ct = default);
}

public class WikiPageQueryService : IWikiPageQueryService
{
    private readonly IAzureDevOpsService _devOps;
    private readonly ILogger<WikiPageQueryService> _logger;

    public WikiPageQueryService(IAzureDevOpsService devOps, ILogger<WikiPageQueryService> logger)
    {
        _devOps = devOps;
        _logger = logger;
    }

    public async Task<List<WikiPageRefMessage>> QueryPageRefsAsync(
        DateTimeOffset? since = null, CancellationToken ct = default)
    {
        var wikis = await _devOps.GetWikiListAsync(ct);
        _logger.LogInformation("[WIKI-QUERY] Found {Count} wiki(s) in project.", wikis.Count);

        var refs = new List<WikiPageRefMessage>();

        foreach (var wiki in wikis)
        {
            if (wiki.Id is null) continue;

            try
            {
                List<string> paths;

                if (since.HasValue)
                {
                    var changedPaths = await _devOps.GetChangedWikiPagePathsAsync(wiki, since.Value, ct);

                    if (changedPaths is null)
                    {
                        // null = brak repositoryId lub brak uprawnień Repos (Read) — fallback do pełnej synchronizacji
                        _logger.LogWarning(
                            "[WIKI-QUERY] Wiki '{Name}' — incremental sync unavailable (missing repositoryId or insufficient PAT permissions), falling back to full sync.",
                            wiki.Name);
                        paths = await _devOps.GetWikiPagePathsAsync(wiki.Id, ct);
                    }
                    else if (changedPaths.Count == 0)
                    {
                        _logger.LogInformation(
                            "[WIKI-QUERY] Wiki '{Name}' — no changes since {Since}.", wiki.Name, since.Value);
                        continue;
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[WIKI-QUERY] Wiki '{Name}' → {Count} changed page(s) since {Since}.",
                            wiki.Name, changedPaths.Count, since.Value);
                        paths = changedPaths;
                    }
                }
                else
                {
                    // Brak watermarku — pełna synchronizacja
                    paths = await _devOps.GetWikiPagePathsAsync(wiki.Id, ct);
                    _logger.LogInformation("[WIKI-QUERY] Wiki '{Name}' → {Count} page path(s) (full sync).",
                        wiki.Name, paths.Count);
                }

                refs.AddRange(paths.Select(path => new WikiPageRefMessage
                {
                    WikiId = wiki.Id,
                    WikiName = wiki.Name,
                    Path = path
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[WIKI-QUERY] Failed to enumerate pages of wiki '{Name}' ({Id}) — skipping.",
                    wiki.Name, wiki.Id);
            }
        }

        return refs;
    }
}
