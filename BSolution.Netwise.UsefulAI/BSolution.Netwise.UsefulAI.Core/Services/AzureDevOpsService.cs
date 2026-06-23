using BSolution.Netwise.UsefulAI.Core.Models;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BSolution.Netwise.UsefulAI.Core.Services;

public interface IAzureDevOpsService
{
    Task<WorkItemDetail> GetWorkItemAsync(int id, CancellationToken ct = default);
    Task<string> AddCommentAsync(int workItemId, string comment, CancellationToken ct = default);
    Task<WikiPageDetail> GetWikiPageAsync(string wikiId, string pagePath, CancellationToken ct = default);

    /// <summary>Executes a WIQL query and returns a list of work item IDs.</summary>
    Task<List<int>> QueryWorkItemIdsAsync(string wiql, CancellationToken ct = default);

    /// <summary>Retrieves details of multiple work items in a single request (max 200 IDs).</summary>
    Task<List<WorkItemDetail>> GetWorkItemsBatchAsync(IEnumerable<int> ids, CancellationToken ct = default);

    /// <summary>Retrieves comments for a single work item (separate endpoint in DevOps API).</summary>
    Task<List<WorkItemComment>> GetWorkItemCommentsAsync(int workItemId, CancellationToken ct = default);

    /// <summary>Returns the list of all wikis in the project.</summary>
    Task<List<WikiInfo>> GetWikiListAsync(CancellationToken ct = default);

    /// <summary>Returns a full, flattened list of wiki page paths (without content).</summary>
    Task<List<string>> GetWikiPagePathsAsync(string wikiId, CancellationToken ct = default);

    /// <summary>
    /// Returns wiki page paths that were modified or added after <paramref name="since"/>.
    /// Uses the Git Commits API (searchCriteria.fromDate) on the repository backing the wiki.
    /// Returns <c>null</c> as a signal to fall back to a full synchronization — may occur when
    /// <c>repositoryId</c> was not returned by the API or the PAT lacks Repos (Read) permissions.
    /// </summary>
    Task<List<string>?> GetChangedWikiPagePathsAsync(WikiInfo wiki, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>
    /// Searches work items using the native DevOps Work Item Search API
    /// (Lucene query syntax — fuzzy, wildcards, field-scoped, boolean ops, BM25).
    /// </summary>
    /// <param name="searchText">Query in Lucene syntax (e.g. "authentication~" or "title:login AND state:Active").</param>
    /// <param name="filters">Per-field DevOps filters. Keys: System.WorkItemType, System.State, System.AreaPath, System.AssignedTo, System.Tags.</param>
    /// <param name="top">Max number of results (1-1000).</param>
    /// <param name="skip">Pagination offset.</param>
    Task<List<WorkItemSearchHit>> SearchWorkItemsByKeywordsAsync(
        string searchText,
        IReadOnlyDictionary<string, string[]>? filters = null,
        int top = 50,
        int skip = 0,
        CancellationToken ct = default);

    // ?? Repos / Pull Requests 

    /// <summary>Returns the list of all Git repositories in the project.</summary>
    Task<List<GitRepositoryInfo>> GetRepositoriesAsync(CancellationToken ct = default);

    /// <summary>Retrieves PR details by its ID and repository name/ID.</summary>
    Task<PullRequestDetail?> GetPullRequestAsync(string repositoryId, int pullRequestId, CancellationToken ct = default);

    /// <summary>List of file changes in a PR (add/edit/delete/rename) relative to the target branch.</summary>
    Task<List<PullRequestChange>> GetPullRequestChangesAsync(string repositoryId, int pullRequestId, CancellationToken ct = default);

    /// <summary>Work items linked to a PR (links in description / commits).</summary>
    Task<List<int>> GetPullRequestWorkItemIdsAsync(string repositoryId, int pullRequestId, CancellationToken ct = default);

    /// <summary>Retrieves the content of a file from a repo at a specific commit (null if deleted/absent).</summary>
    Task<string?> GetFileContentAsync(string repositoryId, string path, string? commitId = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves file content from a repo at a specific BRANCH (HEAD) — used by code scan,
    /// which always reads from the branch fixed in config (e.g. <c>main</c>).
    /// Returns <c>null</c> when the file does not exist on the given branch.
    /// </summary>
    Task<string?> GetFileContentOnBranchAsync(string repositoryId, string path, string branch, CancellationToken ct = default);

    /// <summary>List of files/folders in a repo at the given path (recursively if <paramref name="recursive"/>).</summary>
    Task<List<GitItem>> GetRepositoryItemsAsync(string repositoryId, string path = "/", bool recursive = false, string? commitId = null, CancellationToken ct = default);

    /// <summary>
    /// List of files/folders in a repo from a specific BRANCH (HEAD). Used by code scan —
    /// does not require knowing the commit SHA, always reads the tip of the given branch.
    /// </summary>
    Task<List<GitItem>> GetRepositoryItemsOnBranchAsync(string repositoryId, string branch, string path = "/", bool recursive = true, CancellationToken ct = default);

    /// <summary>
    /// Returns the SHA of the latest commit on the given branch. Uses <c>git/refs</c>
    /// (filter <c>filter=heads/{branch}</c>). Returns <c>null</c> if the branch does not exist.
    /// </summary>
    Task<string?> GetLatestCommitShaAsync(string repositoryId, string branch, CancellationToken ct = default);

    /// <summary>
    /// Returns the list of file changes between two commits (<paramref name="baseSha"/> → <paramref name="targetSha"/>).
    /// Uses the Git <c>diffs/commits</c> API. When <paramref name="baseSha"/> is <c>null</c>, returns <c>null</c>
    /// — this signals the caller to perform a full tree scan.
    /// </summary>
    Task<List<RepoFileChange>?> GetChangesBetweenCommitsAsync(
        string repositoryId, string? baseSha, string targetSha, CancellationToken ct = default);

    // ?? WIKI write ??????????????????????????????????????????????????????????

    /// <summary>
    /// Creates a new wiki page or updates an existing one (PUT /wiki/wikis/{wikiId}/pages).
    /// If the page exists, <paramref name="eTag"/> (If-Match) is required. When <paramref name="eTag"/>
    /// is null, performs best-effort: tries to fetch the current ETag and retries the write.
    /// </summary>
    Task<WikiPageWriteResult> CreateOrUpdateWikiPageAsync(
        string wikiId,
        string pagePath,
        string contentMarkdown,
        string? eTag = null,
        CancellationToken ct = default);
}

public class AzureDevOpsService : IAzureDevOpsService
{
    private readonly HttpClient _http;
    private readonly string _organization;
    private readonly string _project;
    private readonly string _apiVersion = "7.1";

    /// <summary>
    /// The Search API (extension) is hosted under a separate subdomain almsearch.dev.azure.com,
    /// so we cannot use HttpClient.BaseAddress — we send with an absolute URI.
    /// The PAT from the default Authorization header also works on that host.
    /// </summary>
    private readonly Uri _almSearchBaseUri;

    public AzureDevOpsService(IConfiguration config, HttpClient http)
    {
        _organization = config["AzureDevOps:Organization"]!;
        _project = config["AzureDevOps:Project"]!;

        var pat = config["AzureDevOps:PersonalAccessToken"]!;
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));

        _http = http;
        _http.BaseAddress = new Uri($"https://dev.azure.com/{_organization}/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", token);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        _almSearchBaseUri = new Uri($"https://almsearch.dev.azure.com/{_organization}/");
    }

    // ¦¦ Work Items ¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦

    public async Task<WorkItemDetail> GetWorkItemAsync(int id, CancellationToken ct = default)
    {
        var url = $"{_project}/_apis/wit/workitems/{id}?$expand=all&api-version={_apiVersion}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        return MapWorkItemDetail(json!);
    }

    public async Task<string> AddCommentAsync(
        int workItemId, string comment, CancellationToken ct = default)
    {
        var url = $"{_project}/_apis/wit/workItems/{workItemId}/comments" +
                  $"?api-version={_apiVersion}-preview.3";

        var body = new { text = comment };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        var commentId = result?["id"]?.ToString() ?? "unknown";

        return $"Comment #{commentId} posted successfully on work item #{workItemId}. " +
               $"URL: https://dev.azure.com/{_organization}/{_project}/_workitems/edit/{workItemId}";
    }

    public async Task<List<WorkItemComment>> GetWorkItemCommentsAsync(
        int workItemId, CancellationToken ct = default)
    {
        // Comments are not returned by $expand=all — they require a separate endpoint (preview).
        var url = $"{_project}/_apis/wit/workItems/{workItemId}/comments" +
                  $"?api-version={_apiVersion}-preview.4";

        var response = await _http.GetAsync(url, ct);

        // No comments or older project without support — treat as empty list
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);

        return json?["comments"]?.AsArray()
            .OfType<JsonObject>()
            .Select(c => new WorkItemComment
            {
                Id = c["id"]?.GetValue<int>() ?? 0,
                Text = c["text"]?.ToString(),
                CreatedBy = c["createdBy"]?["displayName"]?.ToString(),
                CreatedDate = c["createdDate"]?.GetValue<DateTime?>(),
                ModifiedBy = c["modifiedBy"]?["displayName"]?.ToString(),
                ModifiedDate = c["modifiedDate"]?.GetValue<DateTime?>()
            })
            .OrderBy(c => c.CreatedDate ?? DateTime.MinValue)
            .ToList() ?? [];
    }

    // ¦¦ WIQL ¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦

    public async Task<List<int>> QueryWorkItemIdsAsync(string wiql, CancellationToken ct = default)
    {
        var url = $"{_project}/_apis/wit/wiql?$top=10000&api-version={_apiVersion}";
        var body = new { query = wiql };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(url, content, ct);
        var resStr = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);

        return json?["workItems"]?.AsArray()
            .Select(item => item?["id"]?.GetValue<int>() ?? 0)
            .Where(id => id > 0)
            .ToList() ?? [];
    }

    public async Task<List<WorkItemDetail>> GetWorkItemsBatchAsync(
        IEnumerable<int> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        var result = new List<WorkItemDetail>(idList.Count);

        for (var i = 0; i < idList.Count; i += 200)
        {
            var batchIds = string.Join(",", idList.Skip(i).Take(200));
            var url = $"{_project}/_apis/wit/workitems?ids={batchIds}&$expand=all&api-version={_apiVersion}";

            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
            var items = json?["value"]?.AsArray()
                .OfType<JsonObject>()
                .Select(MapWorkItemDetail)
                .ToList() ?? [];

            result.AddRange(items);
        }

        return result;
    }

    // ¦¦ Work Item Search (Lucene/BM25, almsearch.dev.azure.com) ¦¦¦¦¦¦¦¦¦¦¦¦¦

    public async Task<List<WorkItemSearchHit>> SearchWorkItemsByKeywordsAsync(
        string searchText,
        IReadOnlyDictionary<string, string[]>? filters = null,
        int top = 50,
        int skip = 0,
        CancellationToken ct = default)
    {
        // The Search extension endpoint is hosted on a separate host — use absolute URI.
        var url = new Uri(
            _almSearchBaseUri,
            $"{_project}/_apis/search/workitemsearchresults?api-version={_apiVersion}");

        // DevOps Search always requires the System.TeamProject filter — without it the API returns 400.
        var effectiveFilters = filters is null
            ? new Dictionary<string, string[]>()
            : new Dictionary<string, string[]>(filters);

        if (!effectiveFilters.ContainsKey("System.TeamProject"))
            effectiveFilters["System.TeamProject"] = [_project];

        var body = new Dictionary<string, object?>
        {
            ["searchText"] = searchText,
            ["$top"] = Math.Clamp(top, 1, 1000),
            ["$skip"] = Math.Max(0, skip),
            ["includeFacets"] = false,
            ["filters"] = effectiveFilters
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(req, ct);

        // Search extension may be disabled in on-prem organizations — treat as no results.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            return [];

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);

        return json?["results"]?.AsArray()
            .OfType<JsonObject>()
            .Select(MapWorkItemSearchHit)
            .ToList() ?? [];
    }

    private WorkItemSearchHit MapWorkItemSearchHit(JsonObject result)
    {
        var fields = result["fields"]?.AsObject();

        // Highlights to obiekt: { "system.title": ["...<highlighthit>auth</highlighthit>..."], ... }
        var highlights = new Dictionary<string, List<string>>();
        if (result["hits"]?.AsArray() is { } hits)
        {
            foreach (var hit in hits.OfType<JsonObject>())
            {
                var fieldRef = hit["fieldReferenceName"]?.ToString();
                var highlightArr = hit["highlights"]?.AsArray();
                if (fieldRef is null || highlightArr is null) continue;

                highlights[fieldRef] = highlightArr
                    .Select(h => h?.ToString() ?? string.Empty)
                    .Where(s => s.Length > 0)
                    .ToList();
            }
        }

        var id = int.TryParse(fields?["system.id"]?.ToString(), out var parsedId) ? parsedId : 0;

        return new WorkItemSearchHit
        {
            Id = id,
            Title = fields?["system.title"]?.ToString(),
            Type = fields?["system.workitemtype"]?.ToString(),
            State = fields?["system.state"]?.ToString(),
            AssignedTo = fields?["system.assignedto"]?.ToString(),
            AreaPath = fields?["system.areapath"]?.ToString(),
            Tags = fields?["system.tags"]?.ToString(),
            Highlights = highlights,
            Url = id > 0
                ? $"https://dev.azure.com/{_organization}/{_project}/_workitems/edit/{id}"
                : null
        };
    }

    // ¦¦ WIKI ¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦

    public async Task<WikiPageDetail> GetWikiPageAsync(
        string wikiId, string pagePath, CancellationToken ct = default)
    {
        var encodedPath = Uri.EscapeDataString(pagePath);
        var url = $"{_project}/_apis/wiki/wikis/{wikiId}/pages" +
                  $"?path={encodedPath}&includeContent=true&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);

        // Container pages (folders) may have no content — 404 is not an error
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new WikiPageDetail { Path = pagePath };

        response.EnsureSuccessStatusCode();

        // ETag from the HTTP header — unique version token of the page
        var eTag = response.Headers.ETag?.Tag;
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);

        return new WikiPageDetail
        {
            Id = json?["id"]?.ToString(),
            Path = json?["path"]?.ToString(),
            Content = json?["content"]?.ToString(),
            RemoteUrl = json?["remoteUrl"]?.ToString(),
            GitItemPath = json?["gitItemPath"]?.ToString(),
            ETag = eTag
        };
    }

    public async Task<List<WikiInfo>> GetWikiListAsync(CancellationToken ct = default)
    {
        var url = $"{_project}/_apis/wiki/wikis?api-version={_apiVersion}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);

        return json?["value"]?.AsArray()
            .OfType<JsonObject>()
            .Select(w => new WikiInfo
            {
                Id = w["id"]?.ToString(),
                Name = w["name"]?.ToString(),
                RemoteUrl = w["remoteUrl"]?.ToString(),
                RepositoryId = w["repositoryId"]?.ToString(),
                MappedPath = w["mappedPath"]?.ToString()
            })
            .Where(w => w.Id is not null)
            .ToList() ?? [];
    }

    public async Task<List<string>?> GetChangedWikiPagePathsAsync(
        WikiInfo wiki, DateTimeOffset since, CancellationToken ct = default)
    {
        if (wiki.RepositoryId is null)
        {
            // repositoryId was not returned by the API — fall back to full synchronization
            return null;
        }

        // Git Commits API with fromDate — returns commits since the given date
        var fromDate = Uri.EscapeDataString(since.UtcDateTime.ToString("o"));
        var url = $"{_project}/_apis/git/repositories/{wiki.RepositoryId}/commits" +
                  $"?searchCriteria.fromDate={fromDate}&searchCriteria.itemPath=/&$top=1000&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);

        // No repository or insufficient permissions — fall back to full synchronization
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        var commits = json?["value"]?.AsArray().OfType<JsonObject>().ToList() ?? [];

        if (commits.Count == 0)
            return [];

        // For each commit, fetch the changed files
        var changedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var commit in commits)
        {
            var commitId = commit["commitId"]?.ToString();
            if (commitId is null) continue;

            var changesUrl = $"{_project}/_apis/git/repositories/{wiki.RepositoryId}/commits/{commitId}/changes" +
                             $"?api-version={_apiVersion}";
            var changesResponse = await _http.GetAsync(changesUrl, ct);
            if (!changesResponse.IsSuccessStatusCode) continue;

            var changesJson = await changesResponse.Content.ReadFromJsonAsync<JsonObject>(ct);
            var changes = changesJson?["changes"]?.AsArray().OfType<JsonObject>() ?? [];

            foreach (var change in changes)
            {
                var itemPath = change["item"]?["path"]?.ToString();
                if (itemPath is null) continue;

                // Wiki files have .md extension; convert Git path › wiki path
                // (remove mappedPath prefix and .md extension)
                var wikiPath = GitPathToWikiPath(itemPath, wiki.MappedPath);
                if (wikiPath is not null)
                    changedPaths.Add(wikiPath);
            }
        }

        return [.. changedPaths];
    }

    /// <summary>
    /// Converts a Git file path to a wiki page path.
    /// E.g. "/wiki/Architecture/Overview.md" (mappedPath="/wiki") → "/Architecture/Overview".
    /// </summary>
    private static string? GitPathToWikiPath(string gitPath, string? mappedPath)
    {
        if (!gitPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return null;

        var prefix = string.IsNullOrEmpty(mappedPath) ? string.Empty : mappedPath.TrimEnd('/');
        var relative = gitPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? gitPath[prefix.Length..]
            : gitPath;

        // Remove .md extension
        var wikiPath = relative[..^3];
        return string.IsNullOrEmpty(wikiPath) ? null : wikiPath;
    }

    public async Task<List<string>> GetWikiPagePathsAsync(string wikiId, CancellationToken ct = default)
    {
        // recursionLevel=full returns the full page tree in a single request
        var url = $"{_project}/_apis/wiki/wikis/{wikiId}/pages" +
                  $"?path=/&recursionLevel=full&includeContent=false&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);

        var paths = new List<string>();
        CollectPagePaths(json, paths);
        return paths;
    }

    /// <summary>Recursively flattens the wiki page tree into a list of paths.</summary>
    private static void CollectPagePaths(JsonNode? node, List<string> paths)
    {
        if (node is null) return;

        var path = node["path"]?.GetValue<string>();

        // Skip root "/" — it is a virtual container, not a page with content
        if (path is not null && path != "/")
            paths.Add(path);

        var subPages = node["subPages"]?.AsArray();
        if (subPages is null) return;

        foreach (var subPage in subPages)
            CollectPagePaths(subPage, paths);
    }

    // ¦¦ Mapping ¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦¦

    private WorkItemDetail MapWorkItemDetail(JsonObject json)
    {
        var fields = json["fields"]?.AsObject();

        var relations = json["relations"]?.AsArray()
            ?.Select(r => new WorkItemRelation
            {
                RelationType = r?["attributes"]?["name"]?.ToString(),
                Url = r?["url"]?.ToString(),
                RelatedId = ExtractIdFromUrl(r?["url"]?.ToString())
            })
            .Where(r => r.RelatedId.HasValue)
            .ToList() ?? [];

        return new WorkItemDetail
        {
            Id = json["id"]?.GetValue<int>() ?? 0,
            Title = fields?["System.Title"]?.ToString(),
            Type = fields?["System.WorkItemType"]?.ToString(),
            State = fields?["System.State"]?.ToString(),
            Description = fields?["System.Description"]?.ToString(),
            AcceptanceCriteria = fields?["Microsoft.VSTS.Common.AcceptanceCriteria"]?.ToString(),
            AreaPath = fields?["System.AreaPath"]?.ToString(),
            IterationPath = fields?["System.IterationPath"]?.ToString(),
            Tags = fields?["System.Tags"]?.ToString(),
            Priority = fields?["Microsoft.VSTS.Common.Priority"]?.GetValue<int>(),
            AssignedTo = fields?["System.AssignedTo"]?["displayName"]?.ToString(),
            CreatedDate = fields?["System.CreatedDate"]?.GetValue<DateTime>(),
            ChangedDate = fields?["System.ChangedDate"]?.GetValue<DateTime>(),
            Relations = relations,
            Url = $"https://dev.azure.com/{_organization}/{_project}/_workitems/edit/{json["id"]}"
        };
    }

    private static int? ExtractIdFromUrl(string? url)
    {
        if (url is null) return null;
        var parts = url.Split('/');
        return int.TryParse(parts.LastOrDefault(), out var id) ? id : null;
    }

    // ?? Repos / Pull Requests ???????????????????????????????????????????????

    public async Task<List<GitRepositoryInfo>> GetRepositoriesAsync(CancellationToken ct = default)
    {
        var url = $"{_project}/_apis/git/repositories?api-version={_apiVersion}";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        return json?["value"]?.AsArray()
            .OfType<JsonObject>()
            .Select(r => new GitRepositoryInfo
            {
                Id = r["id"]?.ToString(),
                Name = r["name"]?.ToString(),
                DefaultBranch = r["defaultBranch"]?.ToString(),
                Url = r["url"]?.ToString(),
                WebUrl = r["webUrl"]?.ToString()
            })
            .Where(r => r.Id is not null)
            .ToList() ?? [];
    }

    public async Task<PullRequestDetail?> GetPullRequestAsync(
        string repositoryId, int pullRequestId, CancellationToken ct = default)
    {
        var url = $"{_project}/_apis/git/repositories/{repositoryId}/pullrequests/{pullRequestId}" +
                  $"?api-version={_apiVersion}";
        var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var pr = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        if (pr is null) return null;

        return new PullRequestDetail
        {
            PullRequestId = pr["pullRequestId"]?.GetValue<int>() ?? pullRequestId,
            RepositoryId = pr["repository"]?["id"]?.ToString(),
            RepositoryName = pr["repository"]?["name"]?.ToString(),
            Title = pr["title"]?.ToString(),
            Description = pr["description"]?.ToString(),
            SourceBranch = pr["sourceRefName"]?.ToString(),
            TargetBranch = pr["targetRefName"]?.ToString(),
            Status = pr["status"]?.ToString(),
            MergeStatus = pr["mergeStatus"]?.ToString(),
            CreatedBy = pr["createdBy"]?["displayName"]?.ToString(),
            CreationDate = pr["creationDate"]?.GetValue<DateTime?>(),
            ClosedDate = pr["closedDate"]?.GetValue<DateTime?>(),
            LastMergeCommitId = pr["lastMergeCommit"]?["commitId"]?.ToString(),
            LastMergeSourceCommitId = pr["lastMergeSourceCommit"]?["commitId"]?.ToString(),
            LastMergeTargetCommitId = pr["lastMergeTargetCommit"]?["commitId"]?.ToString(),
            Url = pr["url"]?.ToString(),
            WebUrl = $"https://dev.azure.com/{_organization}/{_project}/_git/{pr["repository"]?["name"]}/pullrequest/{pullRequestId}"
        };
    }

    public async Task<List<PullRequestChange>> GetPullRequestChangesAsync(
        string repositoryId, int pullRequestId, CancellationToken ct = default)
    {
        // PR iterations — we take the latest one and read its change list (PR Changes API).
        var iterUrl = $"{_project}/_apis/git/repositories/{repositoryId}/pullrequests/{pullRequestId}/iterations" +

                      $"?api-version={_apiVersion}";
        var iterResp = await _http.GetAsync(iterUrl, ct);

        if (iterResp.StatusCode == HttpStatusCode.NotFound)
            return [];

        iterResp.EnsureSuccessStatusCode();
        var iterJson = await iterResp.Content.ReadFromJsonAsync<JsonObject>(ct);
        var iterations = iterJson?["value"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
        if (iterations.Count == 0) return [];

        var latestId = iterations.Last()["id"]?.GetValue<int>() ?? 1;

        var changesUrl = $"{_project}/_apis/git/repositories/{repositoryId}/pullrequests/{pullRequestId}/iterations/{latestId}/changes" +
                         $"?$top=2000&api-version={_apiVersion}";
        var changesResp = await _http.GetAsync(changesUrl, ct);
        changesResp.EnsureSuccessStatusCode();

        var changesJson = await changesResp.Content.ReadFromJsonAsync<JsonObject>(ct);

        return changesJson?["changeEntries"]?.AsArray()
            .OfType<JsonObject>()
            .Select(c => new PullRequestChange
            {
                Path = c["item"]?["path"]?.ToString(),
                ChangeType = c["changeType"]?.ToString(),
                OriginalPath = c["originalPath"]?["path"]?.ToString()
            })
            .Where(c => c.Path is not null)
            .ToList() ?? [];
    }

    public async Task<List<int>> GetPullRequestWorkItemIdsAsync(
        string repositoryId, int pullRequestId, CancellationToken ct = default)
    {
        var url = $"{_project}/_apis/git/repositories/{repositoryId}/pullrequests/{pullRequestId}/workitems" +
                  $"?api-version={_apiVersion}";
        var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);

        return json?["value"]?.AsArray()
            .Select(w => int.TryParse(w?["id"]?.ToString(), out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList() ?? [];
    }

    public async Task<string?> GetFileContentAsync(
        string repositoryId, string path, string? commitId = null, CancellationToken ct = default)
    {
        // includeContent=true returns content in JSON. For binary files $format=octetStream would be needed —
        // here we assume text files (code, markdown).
        var versionParam = commitId is null
            ? string.Empty
            : $"&versionDescriptor.version={commitId}&versionDescriptor.versionType=commit";

        var url = $"{_project}/_apis/git/repositories/{repositoryId}/items" +
                  $"?path={Uri.EscapeDataString(path)}&includeContent=true{versionParam}" +
                  $"&api-version={_apiVersion}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(req, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        return json?["content"]?.ToString();
    }

    public async Task<List<GitItem>> GetRepositoryItemsAsync(
        string repositoryId, string path = "/", bool recursive = false,
        string? commitId = null, CancellationToken ct = default)
    {
        var recursionLevel = recursive ? "full" : "oneLevel";
        var versionParam = commitId is null
            ? string.Empty
            : $"&versionDescriptor.version={commitId}&versionDescriptor.versionType=commit";

        var url = $"{_project}/_apis/git/repositories/{repositoryId}/items" +
                  $"?scopePath={Uri.EscapeDataString(path)}&recursionLevel={recursionLevel}" +
                  $"&includeContentMetadata=false{versionParam}&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);

        return json?["value"]?.AsArray()
            .OfType<JsonObject>()
            .Select(i => new GitItem
            {
                Path = i["path"]?.ToString(),
                GitObjectType = i["gitObjectType"]?.ToString(),
                IsFolder = i["isFolder"]?.GetValue<bool>() ?? false,
                ObjectId = i["objectId"]?.ToString(),
                Url = i["url"]?.ToString()
            })
            .Where(i => i.Path is not null)
            .ToList() ?? [];
    }

    public async Task<string?> GetFileContentOnBranchAsync(
        string repositoryId, string path, string branch, CancellationToken ct = default)
    {
        var url = $"{_project}/_apis/git/repositories/{repositoryId}/items" +
                  $"?path={Uri.EscapeDataString(path)}&includeContent=true" +
                  $"&versionDescriptor.version={Uri.EscapeDataString(branch)}" +
                  $"&versionDescriptor.versionType=branch" +
                  $"&api-version={_apiVersion}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(req, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        return json?["content"]?.ToString();
    }

    public async Task<List<GitItem>> GetRepositoryItemsOnBranchAsync(
        string repositoryId, string branch, string path = "/", bool recursive = true,
        CancellationToken ct = default)
    {
        var recursionLevel = recursive ? "full" : "oneLevel";
        var url = $"{_project}/_apis/git/repositories/{repositoryId}/items" +
                  $"?scopePath={Uri.EscapeDataString(path)}&recursionLevel={recursionLevel}" +
                  $"&includeContentMetadata=true" +
                  $"&versionDescriptor.version={Uri.EscapeDataString(branch)}" +
                  $"&versionDescriptor.versionType=branch" +
                  $"&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);

        return json?["value"]?.AsArray()
            .OfType<JsonObject>()
            .Select(i => new GitItem
            {
                Path = i["path"]?.ToString(),
                GitObjectType = i["gitObjectType"]?.ToString(),
                IsFolder = i["isFolder"]?.GetValue<bool>() ?? false,
                ObjectId = i["objectId"]?.ToString(),
                Url = i["url"]?.ToString(),
                Size = i["contentMetadata"]?["fileLength"]?.GetValue<long?>()
                       ?? i["size"]?.GetValue<long?>()
            })
            .Where(i => i.Path is not null)
            .ToList() ?? [];
    }

    public async Task<string?> GetLatestCommitShaAsync(
        string repositoryId, string branch, CancellationToken ct = default)
    {
        // Refs API: filter omits the "refs/" prefix — "heads/{branch}" is sufficient.
        var url = $"{_project}/_apis/git/repositories/{repositoryId}/refs" +
                  $"?filter={Uri.EscapeDataString($"heads/{branch}")}" +
                  $"&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        var refs = json?["value"]?.AsArray().OfType<JsonObject>().ToList() ?? [];

        // Full name "refs/heads/{branch}" — compared precisely.
        var full = $"refs/heads/{branch}";
        var match = refs.FirstOrDefault(r =>
            string.Equals(r["name"]?.ToString(), full, StringComparison.OrdinalIgnoreCase));

        return match?["objectId"]?.ToString();
    }

    public async Task<List<RepoFileChange>?> GetChangesBetweenCommitsAsync(
        string repositoryId, string? baseSha, string targetSha, CancellationToken ct = default)
    {
        // Without a base SHA we cannot compute a diff — signal full-scan.
        if (string.IsNullOrEmpty(baseSha))
            return null;

        if (string.Equals(baseSha, targetSha, StringComparison.OrdinalIgnoreCase))
            return [];

        // Git diffs/commits API returns batches of up to ~1000 changes; we use a generous $top
        // and will add pagination if it starts truncating.
        var url = $"{_project}/_apis/git/repositories/{repositoryId}/diffs/commits" +
                  $"?baseVersion={baseSha}&baseVersionType=commit" +
                  $"&targetVersion={targetSha}&targetVersionType=commit" +
                  $"&$top=2000" +
                  $"&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            return null;

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);

        return json?["changes"]?.AsArray()
            .OfType<JsonObject>()
            .Select(c => new RepoFileChange
            {
                Path = c["item"]?["path"]?.ToString(),
                OriginalPath = c["originalPath"]?.ToString()
                               ?? c["sourceServerItem"]?.ToString(),
                ChangeType = c["changeType"]?.ToString()
            })
            .Where(c => c.Path is not null)
            .ToList() ?? [];
    }

    // ?? WIKI write ??????????????????????????????????????????????????????????

    public async Task<WikiPageWriteResult> CreateOrUpdateWikiPageAsync(
        string wikiId, string pagePath, string contentMarkdown,
        string? eTag = null, CancellationToken ct = default)
    {
        // PUT /wiki/wikis/{wikiId}/pages?path={path} with optional If-Match (ETag).
        // No If-Match → creates a new page (returns 201). When the page exists,
        // If-Match is required. If eTag is null and the page exists, we first fetch
        // the current ETag and retry the write (race-free best-effort).
        if (eTag is null)
        {
            var current = await GetWikiPageAsync(wikiId, pagePath, ct);
            if (current.Id is not null)
                eTag = current.ETag;
        }

        var result = await PutWikiPageAsync(wikiId, pagePath, contentMarkdown, eTag, ct);

        // 412 = mismatched ETag — signal to retry with a refreshed ETag.
        if (result is null)
        {
            var refreshed = await GetWikiPageAsync(wikiId, pagePath, ct);
            result = await PutWikiPageAsync(wikiId, pagePath, contentMarkdown, refreshed.ETag, ct)
                ?? throw new InvalidOperationException(
                    $"Failed to write wiki page '{pagePath}' after ETag refresh.");
        }

        return result;
    }

    private async Task<WikiPageWriteResult?> PutWikiPageAsync(
        string wikiId, string pagePath, string contentMarkdown, string? eTag, CancellationToken ct)
    {
        var encodedPath = Uri.EscapeDataString(pagePath);
        var url = $"{_project}/_apis/wiki/wikis/{wikiId}/pages" +
                  $"?path={encodedPath}&api-version={_apiVersion}";

        var body = new { content = contentMarkdown };
        using var req = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrEmpty(eTag))
            req.Headers.TryAddWithoutValidation("If-Match", eTag);

        using var response = await _http.SendAsync(req, ct);

        // 412 = mismatched ETag — signal to retry with a refreshed ETag.
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            return null;

        response.EnsureSuccessStatusCode();

        var newEtag = response.Headers.ETag?.Tag;
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);

        return new WikiPageWriteResult
        {
            Path = json?["path"]?.ToString() ?? pagePath,
            ETag = newEtag,
            RemoteUrl = json?["remoteUrl"]?.ToString(),
            Created = response.StatusCode == HttpStatusCode.Created
        };
    }}    }}