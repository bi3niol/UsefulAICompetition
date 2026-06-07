using Microsoft.Extensions.AI;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

public class ResearchTools(
    GetPullRequestDetailsTool prDetails,
    GetPullRequestChangesTool prChanges,
    ReadRepositoryFileTool readFile,
    AnalyzeRepositoryFileTool analyzeFile,
    ListWikiPagesTool listWiki,
    GetWikiPageTool getWiki,
    GetWorkItemDetailsTool getWorkItem,
    ListCodeRepositoriesTool listCodeRepos,
    ListRepositoryFilesTool listRepoFiles)
{
    public IList<AITool> GetAll() =>
    [
        AIFunctionFactory.Create(prDetails.GetPullRequestDetailsAsync),
        AIFunctionFactory.Create(prChanges.GetPullRequestChangesAsync),
        AIFunctionFactory.Create(analyzeFile.AnalyzeRepositoryFileAsync),
        AIFunctionFactory.Create(readFile.ReadRepositoryFileAsync),
        AIFunctionFactory.Create(listWiki.ListWikiPagesAsync),
        AIFunctionFactory.Create(getWiki.GetWikiPageAsync),
        AIFunctionFactory.Create(getWorkItem.GetWorkItemDetailsAsync),
        AIFunctionFactory.Create(listCodeRepos.ListCodeRepositoriesAsync),
        AIFunctionFactory.Create(listRepoFiles.ListRepositoryFilesAsync)
    ];
}
