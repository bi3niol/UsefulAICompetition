using BSolution.Netwise.UsefulAI.Core.Services;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

public class ReadRepositoryFileTool(IAzureDevOpsService devOps)
{
    private const int MaxLength = 20_000;

    [AgentTool(Description = """
        Reads the text content of a single file from a repository at the given commit
        (or HEAD of the default branch when commitId is empty). Use this to understand
        what the code actually does — never assume from the path alone.
        Returns a JSON object with content trimmed to ~20k characters.
        """)]
    public async Task<string> ReadRepositoryFileAsync(
        [Description("Repository ID (GUID)")] string repositoryId,
        [Description("File path inside the repository, e.g. /src/Foo/Bar.cs")] string path,
        [Description("Commit SHA to read at. Pass empty string for default branch HEAD.")]
        string commitId)
    {
        var effectiveCommit = string.IsNullOrWhiteSpace(commitId) ? null : commitId;
        var content = await devOps.GetFileContentAsync(repositoryId, path, effectiveCommit);

        if (content is null)
            return JsonSerializer.Serialize(new { error = "File not found.", path });

        var truncated = content.Length > MaxLength;
        if (truncated) content = content[..MaxLength];

        return JsonSerializer.Serialize(new { path, truncated, content });
    }
}
