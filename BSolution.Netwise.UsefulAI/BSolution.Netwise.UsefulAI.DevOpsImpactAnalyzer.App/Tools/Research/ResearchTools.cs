using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;

public class ResearchTools(
 SearchWorkItemsTool searchWorkItems,
 SearchWikiTool searchWiki,
 GetWorkItemDetailsTool getWorkItemDetails)
{
    public IEnumerable<AITool> GetAll() =>
    [
        AITool.FromMethod(searchWorkItems.SearchWorkItemsAsync),
        AITool.FromMethod(searchWiki.SearchWikiAsync),
        AITool.FromMethod(getWorkItemDetails.GetWorkItemDetailsAsync)
    ];
}
