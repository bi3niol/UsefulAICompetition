using BSolution.Netwise.UsefulAI.Core.Services;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Models;
using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Services;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

/// <summary>
/// Lists files in a repository on the configured branch (filtered by extensions/excluded folders).
/// Researcher uses this to understand project structure before reading individual files.
/// </summary>
public class ListRepositoryFilesTool(
    IAzureDevOpsService devOps,
    CodeRepositoryResolver resolver,
    IOptions<CodeScanOptions> options)
{
    [AgentTool(Description = """
        Lists all code files in a repository on the configured branch.
        Results are filtered by allowed extensions and excluded folders.
        Returns paths grouped by directory. Use this to understand project structure.
        """)]
    public async Task<string> ListRepositoryFilesAsync(
        [Description("Repository ID (GUID) from ListCodeRepositories")] string repositoryId)
    {
        var repos = await resolver.ResolveAllAsync();
        var repo = repos.FirstOrDefault(r =>
            string.Equals(r.Id, repositoryId, StringComparison.OrdinalIgnoreCase));

        if (repo is null)
            return JsonSerializer.Serialize(new { error = "Repository not configured for code scan.", repositoryId });

        var allItems = await devOps.GetRepositoryItemsOnBranchAsync(repositoryId, repo.Branch);
        var filtered = CodeFileFilter.FilterTree(allItems, options.Value).ToList();

        return JsonSerializer.Serialize(new
        {
            repositoryId,
            branch = repo.Branch,
            totalFiles = filtered.Count,
            files = filtered.Select(f => f.Path).Order()
        });
    }
}
