using Microsoft.Extensions.AI;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;

public class ResearchTools(
 SearchWorkItemsTool searchWorkItems,
 SearchWikiTool searchWiki,
 GetWorkItemDetailsTool getWorkItemDetails,
 GetWikiPageDetailsTool getWikiPageDetails)
{
    public IList<AITool> GetAll() =>
    [
        AIFunctionFactory.Create(searchWorkItems.SearchWorkItemsAsync),
        AIFunctionFactory.Create(searchWiki.SearchWikiAsync),
        AIFunctionFactory.Create(getWorkItemDetails.GetWorkItemDetailsAsync),
        AIFunctionFactory.Create(getWikiPageDetails.GetWikiPageDetailsAsync)
    ];
}
