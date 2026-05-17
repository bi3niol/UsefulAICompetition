using Microsoft.Extensions.AI;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

public class ResearchTools(
    GetPullRequestDetailsTool prDetails,
    GetPullRequestChangesTool prChanges,
    ReadRepositoryFileTool readFile,
    ListWikiPagesTool listWiki,
    GetWikiPageTool getWiki,
    GetWorkItemDetailsTool getWorkItem)
{
    public IList<AITool> GetAll() =>
    [
        AIFunctionFactory.Create(prDetails.GetPullRequestDetailsAsync),
        AIFunctionFactory.Create(prChanges.GetPullRequestChangesAsync),
        AIFunctionFactory.Create(readFile.ReadRepositoryFileAsync),
        AIFunctionFactory.Create(listWiki.ListWikiPagesAsync),
        AIFunctionFactory.Create(getWiki.GetWikiPageAsync),
        AIFunctionFactory.Create(getWorkItem.GetWorkItemDetailsAsync)
    ];
}
