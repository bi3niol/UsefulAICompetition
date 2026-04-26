using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;
using System.ComponentModel;
using System.Text.Json;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Research;

public class KeywordSearchWorkItemsTool(IAzureDevOpsService devOps)
{
    [AgentTool(Description = """
        Searches Azure DevOps work items using the NATIVE DevOps Work Item Search API
        (Lucene/BM25 keyword index — fuzzy match, wildcards, field-scoped queries, boolean operators).

        Use this tool INSTEAD OF the semantic search when the query is dominated by:
        - exact identifiers (e.g. work item IDs as text, ticket codes "INC-1234")
        - error codes / exception names ("CS1061", "ORA-00942", "NullReferenceException")
        - class / method / file names ("UserService.Login", "appsettings.json")
        - product / feature codenames or acronyms unlikely to share an embedding neighbourhood ("MFA", "SSO", "K8s")
        - typo-tolerant lookup (use trailing '~' for fuzzy: "authetication~")

        Lucene query syntax examples:
        - "oauth AND (saml OR jwt) NOT deprecated"
        - "title:login AND state:Active"
        - "\"single sign on\""           (exact phrase)
        - "auth*"                        (prefix wildcard)
        - "login~2"                      (fuzzy, edit distance 2)

        Returns matched work items with highlighted snippets showing WHERE the match occurred.
        For semantic / meaning-based search, prefer SearchWorkItemsAsync instead.
        """)]
    public async Task<string> KeywordSearchWorkItemsAsync(
        [Description("Lucene query (supports fuzzy '~', wildcards '*' '?', boolean AND/OR/NOT, phrases, field-scoping like 'title:foo').")]
        string luceneQuery,

        [Description("Filter by work item type. Comma-separated, e.g. 'User Story,Bug'. Use 'all' for no filter. Default: 'all'.")]
        string itemTypes = "all",

        [Description("Filter by state. Comma-separated, e.g. 'Active,New'. Use 'all' for no filter. Default: 'all'.")]
        string states = "all",

        [Description("Filter by area path (exact match, hierarchical). Empty = no filter. Default: ''.")]
        string areaPath = "",

        [Description("Maximum number of results to return (1-1000). Default: 25.")]
        int maxResults = 25)
    {
        var filters = new Dictionary<string, string[]>();

        if (!string.Equals(itemTypes, "all", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(itemTypes))
        {
            filters["System.WorkItemType"] = SplitCsv(itemTypes);
        }

        if (!string.Equals(states, "all", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(states))
        {
            filters["System.State"] = SplitCsv(states);
        }

        if (!string.IsNullOrWhiteSpace(areaPath))
            filters["System.AreaPath"] = [areaPath];

        var hits = await devOps.SearchWorkItemsByKeywordsAsync(
            searchText: luceneQuery,
            filters: filters,
            top: Math.Clamp(maxResults, 1, 1000));

        if (hits.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                query = luceneQuery,
                message = "No work items matched this Lucene query.",
                results = Array.Empty<object>()
            });
        }

        return JsonSerializer.Serialize(new
        {
            query = luceneQuery,
            totalFound = hits.Count,
            results = hits.Select(h => new
            {
                id = h.Id,
                title = h.Title,
                type = h.Type,
                state = h.State,
                assignedTo = h.AssignedTo,
                areaPath = h.AreaPath,
                tags = h.Tags,
                // Highlights pokazują LLM gdzie konkretnie nastąpiło dopasowanie tokena —
                // pomaga ocenić trafność wyniku bez pobierania pełnej treści WI.
                matchedFragments = h.Highlights,
                url = h.Url
            })
        });
    }

    private static string[] SplitCsv(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
