using BSolution.Netwise.UsefulAI.Core.Models;
using BSolution.Netwise.UsefulAI.Core.Services;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.WikiDocGenerator.App.Tools.Research;

/// <summary>
/// Wspólne narzędzie z Impact Analyzerem — czyta szczegóły work itemu wraz z komentarzami,
/// żeby Researcher rozumiał INTENCJĘ Feature/PBI stojącą za zmianami w kodzie.
/// </summary>
public class GetWorkItemDetailsTool(IAzureDevOpsService devOps)
{
    [AgentTool(Description = """
        Retrieves FULL details of a work item (Feature / PBI / User Story / Bug) including
        description, acceptance criteria and comments. Use this for every work item linked
        to the PR — descriptions and comments often reveal the feature intent that should
        be reflected in the wiki documentation.
        """)]
    public async Task<string> GetWorkItemDetailsAsync(
        [Description("The numeric ID of the work item")] int workItemId)
    {
        WorkItemDetail item;
        try
        {
            item = await devOps.GetWorkItemAsync(workItemId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return JsonSerializer.Serialize(new { error = "Work item not found.", workItemId });
        }

        List<WorkItemComment> comments;
        try
        {
            comments = await devOps.GetWorkItemCommentsAsync(workItemId);
        }
        catch
        {
            comments = [];
        }

        return JsonSerializer.Serialize(new
        {
            id = item.Id,
            type = item.Type,
            title = item.Title,
            state = item.State,
            areaPath = item.AreaPath,
            description = item.Description,
            acceptanceCriteria = item.AcceptanceCriteria,
            url = item.Url,
            comments = comments.Select(c => new { c.CreatedBy, c.CreatedDate, c.Text })
        });
    }
}
