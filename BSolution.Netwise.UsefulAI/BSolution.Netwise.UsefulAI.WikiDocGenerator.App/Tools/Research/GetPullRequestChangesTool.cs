using BSolution.Netwise.UsefulAI.Core.Services;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

public class GetPullRequestChangesTool(IAzureDevOpsService devOps)
{
    [AgentTool(Description = """
        Returns the list of files changed in a pull request (path + change type:
        add / edit / delete / rename). Use this to figure out which areas of
        the codebase the PR touches and which wiki pages may need updates.
        """)]
    public async Task<string> GetPullRequestChangesAsync(
        [Description("Repository ID (GUID)")] string repositoryId,
        [Description("Numeric pull request ID")] int pullRequestId)
    {
        var changes = await devOps.GetPullRequestChangesAsync(repositoryId, pullRequestId);

        return JsonSerializer.Serialize(new
        {
            count = changes.Count,
            changes = changes.Select(c => new
            {
                path = c.Path,
                changeType = c.ChangeType,
                originalPath = c.OriginalPath
            })
        });
    }
}
