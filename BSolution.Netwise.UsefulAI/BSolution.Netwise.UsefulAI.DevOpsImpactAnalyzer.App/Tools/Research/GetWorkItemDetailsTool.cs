using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using Microsoft.Agents.AI;
using System.ComponentModel;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;

public class GetWorkItemDetailsTool(IAzureDevOpsService devOpsService)
{
    [AgentTool(Description = """
        Retrieves FULL details of a specific work item by its ID.
        Use this for the top 3-5 most similar work items found in search 
        to get complete description, acceptance criteria and existing relations.
        Returns: title, type, state, full description, acceptance criteria,
                 area path, tags, priority, assignee and linked items.
        """)]
    public async Task<string> GetWorkItemDetailsAsync(
        [Description("The numeric ID of the work item (e.g. 234)")]
        int workItemId)
    {
        WorkItemDetail item;

        try
        {
            item = await devOpsService.GetWorkItemAsync(workItemId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Work item #{workItemId} not found",
                workItemId
            });
        }

        return JsonSerializer.Serialize(new
        {
            id = item.Id,
            title = item.Title,
            type = item.Type,
            state = item.State,
            priority = item.Priority,
            assignedTo = item.AssignedTo,
            areaPath = item.AreaPath,
            iterationPath = item.IterationPath,
            tags = item.Tags,
            description = item.Description,
            acceptanceCriteria = item.AcceptanceCriteria,
            createdDate = item.CreatedDate,
            changedDate = item.ChangedDate,
            existingRelations = item.Relations?.Select(r => new
            {
                relationType = r.RelationType,
                relatedItemId = r.RelatedId,
                url = r.Url
            }),
            url = item.Url
        }, JsonOptions.Pretty);
    }
}