using BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Services;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

/// <summary>
/// Zwraca listę repozytoriów zarejestrowanych w konfigu do dokumentacji wraz
/// z gałęzią, z której zawsze czytamy kod. Researcher MUSI używać tej listy
/// — nie wybiera repo ani gałęzi sam.
/// </summary>
public class ListCodeRepositoriesTool(CodeRepositoryResolver resolver)
{
    [AgentTool(Description = """
        Returns the list of source code repositories registered for documentation,
        with the branch each one is read from. Use the returned repositoryName
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
