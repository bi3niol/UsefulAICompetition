using BSolution.Netwise.UsefulAI.Core.Services;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Services;

public class CodeRepositoryResolver
{
    private readonly IAzureDevOpsService _devOps;
    private readonly CodeScanOptions _options;
    private readonly ILogger<CodeRepositoryResolver> _logger;
    private List<ResolvedRepository>? _cache;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public CodeRepositoryResolver(
        IAzureDevOpsService devOps,
        IOptions<CodeScanOptions> options,
        ILogger<CodeRepositoryResolver> logger)
    {
        _devOps = devOps;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ResolvedRepository>> ResolveAllAsync(CancellationToken ct = default)
    {
        if (_cache is not null) return _cache;

        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cache is not null) return _cache;

            if (_options.Repositories.Count == 0)
            {
                _cache = [];
                return _cache;
            }

            var allRepos = await _devOps.GetRepositoriesAsync(ct);
            var byName = allRepos
                .Where(r => r.Name is not null && r.Id is not null)
                .ToDictionary(r => r.Name!, r => r, StringComparer.OrdinalIgnoreCase);
            var byId = allRepos
                .Where(r => r.Id is not null)
                .ToDictionary(r => r.Id!, r => r, StringComparer.OrdinalIgnoreCase);

            var resolved = new List<ResolvedRepository>(_options.Repositories.Count);

            foreach (var cfg in _options.Repositories)
            {
                if (string.IsNullOrWhiteSpace(cfg.Name)) continue;

                if (!byName.TryGetValue(cfg.Name, out var repo) &&
                    !byId.TryGetValue(cfg.Name, out repo))
                {
                    _logger.LogWarning("[CODE-RESOLVER] Repository '{Name}' not found — skipping.", cfg.Name);
                    continue;
                }

                var branch = !string.IsNullOrWhiteSpace(cfg.Branch)
                    ? cfg.Branch!
                    : NormalizeDefaultBranch(repo.DefaultBranch);

                if (string.IsNullOrEmpty(branch)) continue;

                resolved.Add(new ResolvedRepository(Id: repo.Id!, Name: repo.Name ?? cfg.Name, Branch: branch));
            }

            _cache = resolved;
            return _cache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static string? NormalizeDefaultBranch(string? full)
    {
        if (string.IsNullOrEmpty(full)) return null;
        const string prefix = "refs/heads/";
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? full[prefix.Length..]
            : full;
    }
}

public record ResolvedRepository(string Id, string Name, string Branch);
