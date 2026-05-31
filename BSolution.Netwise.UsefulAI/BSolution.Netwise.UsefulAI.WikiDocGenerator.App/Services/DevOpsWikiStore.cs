using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Services;

/// <summary>
/// Docelowa implementacja wiki store operująca na Azure DevOps Wiki API.
/// Wymaga skonfigurowanego <c>WikiDocGenerator:TargetWikiName</c> (lub <c>TargetWikiId</c>).
/// </summary>
public class DevOpsWikiStore : IWikiStore
{
    private readonly IAzureDevOpsService _devOps;
    private readonly IConfiguration _config;
    private readonly ILogger<DevOpsWikiStore> _logger;
    private string? _cachedWikiId;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const string DefaultWikiName = "WikiAutoGenDoc";

    public DevOpsWikiStore(IAzureDevOpsService devOps, IConfiguration config, ILogger<DevOpsWikiStore> logger)
    {
        _devOps = devOps;
        _config = config;
        _logger = logger;
    }

    public async Task<List<string>> ListPagePathsAsync(CancellationToken ct = default)
    {
        var wikiId = await ResolveWikiIdAsync(ct);
        return await _devOps.GetWikiPagePathsAsync(wikiId, ct);
    }

    public async Task<WikiPageDetail> GetPageAsync(string path, CancellationToken ct = default)
    {
        var wikiId = await ResolveWikiIdAsync(ct);
        return await _devOps.GetWikiPageAsync(wikiId, path, ct);
    }

    public async Task<WikiPageWriteResult> UpsertPageAsync(string path, string markdownContent, string? eTag = null, CancellationToken ct = default)
    {
        var wikiId = await ResolveWikiIdAsync(ct);
        var result = await _devOps.CreateOrUpdateWikiPageAsync(wikiId, path, markdownContent, eTag, ct);

        _logger.LogInformation("Wiki page {Action} via DevOps API: {Path}", result.Created ? "created" : "updated", path);
        return result;
    }

    private async Task<string> ResolveWikiIdAsync(CancellationToken ct)
    {
        if (_cachedWikiId is not null)
            return _cachedWikiId;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedWikiId is not null)
                return _cachedWikiId;

            var explicitId = _config["WikiDocGenerator:TargetWikiId"];
            if (!string.IsNullOrWhiteSpace(explicitId))
            {
                _cachedWikiId = explicitId;
                return _cachedWikiId;
            }

            var wikiName = _config["WikiDocGenerator:TargetWikiName"] ?? DefaultWikiName;

            var wikis = await _devOps.GetWikiListAsync(ct);
            var existing = wikis.FirstOrDefault(w =>
                string.Equals(w.Name, wikiName, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _logger.LogInformation("Resolved target wiki '{Name}' -> {Id}", wikiName, existing.Id);
                _cachedWikiId = existing.Id!;
                return _cachedWikiId;
            }

            throw new InvalidOperationException(
                $"Wiki '{wikiName}' not found in project. " +
                $"Create it manually in Azure DevOps (Repos → 'New repository' named '{wikiName}', " +
                $"then Overview → Wiki → 'Publish code as wiki' selecting that repo with branch 'main' and path '/'), " +
                $"or set WikiDocGenerator:TargetWikiId directly.");
        }
        finally
        {
            _lock.Release();
        }
    }
}
