using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.Core.Services;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

public class GetPullRequestDetailsTool(IAzureDevOpsService devOps)
{
    [AgentTool(Description = """
        Retrieves details for a pull request: title, description, branches,
        merge commit, author and the IDs of work items linked to the PR.
        Use this once at the beginning of research for a PR-triggered run.
        """)]
    public async Task<string> GetPullRequestDetailsAsync(
        [Description("Repository ID (GUID) the PR belongs to")] string repositoryId,
        [Description("Numeric pull request ID")] int pullRequestId)
    {
        var pr = await devOps.GetPullRequestAsync(repositoryId, pullRequestId);
        if (pr is null)
            return JsonSerializer.Serialize(new { error = $"PR #{pullRequestId} not found.", repositoryId });

        var linkedIds = await devOps.GetPullRequestWorkItemIdsAsync(repositoryId, pullRequestId);

        return JsonSerializer.Serialize(new
        {
            id = pr.PullRequestId,
            repositoryId = pr.RepositoryId,
            repositoryName = pr.RepositoryName,
            title = pr.Title,
            description = pr.Description,
            sourceBranch = pr.SourceBranch,
            targetBranch = pr.TargetBranch,
            status = pr.Status,
            mergeStatus = pr.MergeStatus,
            createdBy = pr.CreatedBy,
            mergeCommitId = pr.LastMergeCommitId,
            url = pr.WebUrl,
            linkedWorkItemIds = linkedIds
        });
    }
}
