using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing.Messages;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Indexing;

/// <summary>
/// Serwis odpowiedzialny wyłącznie za enumerację wszystkich stron WIKI w projekcie
/// (lista wiki + płaska lista ścieżek per wiki).
/// </summary>
public interface IWikiPageQueryService
{
    Task<List<WikiPageRefMessage>> QueryAllPageRefsAsync(CancellationToken ct = default);
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

    public async Task<List<WikiPageRefMessage>> QueryAllPageRefsAsync(CancellationToken ct = default)
    {
        var wikis = await _devOps.GetWikiListAsync(ct);
        _logger.LogInformation("[WIKI-QUERY] Found {Count} wiki(s) in project.", wikis.Count);

        var refs = new List<WikiPageRefMessage>();

        foreach (var wiki in wikis)
        {
            if (wiki.Id is null) continue;

            try
            {
                // recursionLevel=full → wszystkie ścieżki w jednym żądaniu
                var paths = await _devOps.GetWikiPagePathsAsync(wiki.Id, ct);
                _logger.LogInformation("[WIKI-QUERY] Wiki '{Name}' → {Count} page path(s).",
                    wiki.Name, paths.Count);

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
