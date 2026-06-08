using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.Core.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;

public class GetWorkItemDetailsTool(
    IAzureDevOpsService devOpsService,
    ILogger<GetWorkItemDetailsTool> logger)
{
    [AgentTool(Description = """
        Retrieves FULL details of a specific work item by its ID, including comments/discussion.
        Use this for the top 3-5 most similar work items found in search 
        to get complete description, acceptance criteria, existing relations and team discussion.
        Returns: title, type, state, full description, acceptance criteria,
                 area path, tags, priority, assignee, linked items and comments.
        """)]
    public async Task<string> GetWorkItemDetailsAsync(
        [Description("The numeric ID of the work item (e.g. 234)")]
        int workItemId)
    {
        logger.LogInformation("[TOOL] GetWorkItemDetails called for WI#{WorkItemId}", workItemId);

        WorkItemDetail item;

        try
        {
            item = await devOpsService.GetWorkItemAsync(workItemId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("[TOOL] GetWorkItemDetails — WI#{WorkItemId} not found", workItemId);
            return JsonSerializer.Serialize(new
            {
                error = $"Work item #{workItemId} not found",
                workItemId
            });
        }

        List<WorkItemComment> comments;
        try
        {
            comments = await devOpsService.GetWorkItemCommentsAsync(workItemId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[TOOL] GetWorkItemDetails — failed to fetch comments for WI#{WorkItemId}", workItemId);
            comments = [];
        }

        logger.LogInformation("[TOOL] GetWorkItemDetails — WI#{WorkItemId} fetched: type={Type}, state={State}, comments={CommentCount}",
            workItemId, item.Type, item.State, comments.Count);

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
            comments = comments.Select(c => new
            {
                author = c.CreatedBy,
                date = c.CreatedDate,
                text = c.Text
            }),
            url = item.Url
        });
    }
}