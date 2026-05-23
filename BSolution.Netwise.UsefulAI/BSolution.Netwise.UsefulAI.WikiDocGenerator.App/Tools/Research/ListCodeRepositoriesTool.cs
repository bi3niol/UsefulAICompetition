using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Services;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

public class ListCodeRepositoriesTool(CodeRepositoryResolver resolver)
{
    [AgentTool(Description = """
        Returns the list of source code repositories registered for documentation,
        with the branch each one is read from. Use the returned repositoryId
        and branch verbatim when calling other code tools.
        """)]
    public async Task<string> ListCodeRepositoriesAsync()
    {
        var repos = await resolver.ResolveAllAsync();
        return JsonSerializer.Serialize(new
        {
            count = repos.Count,
            repositories = repos.Select(r => new
            {
                repositoryId = r.Id,
                repositoryName = r.Name,
                branch = r.Branch
            })
        });
    }
}
