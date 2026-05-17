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

    /// <summary>Wykonuje zapytanie WIQL i zwraca listê ID work itemów.</summary>
    Task<List<int>> QueryWorkItemIdsAsync(string wiql, CancellationToken ct = default);

    /// <summary>Pobiera szczegó³y wielu work itemów w jednym ¿¹daniu (max 200 ID).</summary>
    Task<List<WorkItemDetail>> GetWorkItemsBatchAsync(IEnumerable<int> ids, CancellationToken ct = default);

    /// <summary>Pobiera komentarze dla pojedynczego work itemu (osobny endpoint w DevOps API).</summary>
    Task<List<WorkItemComment>> GetWorkItemCommentsAsync(int workItemId, CancellationToken ct = default);

    /// <summary>Zwraca listê wszystkich wiki w projekcie.</summary>
    Task<List<WikiInfo>> GetWikiListAsync(CancellationToken ct = default);

    /// <summary>Zwraca pe³n¹, sp³aszczon¹ listê œcie¿ek stron wiki (bez treœci).</summary>
    Task<List<string>> GetWikiPagePathsAsync(string wikiId, CancellationToken ct = default);

    /// <summary>
    /// Zwraca œcie¿ki stron wiki zmodyfikowanych lub dodanych po <paramref name="since"/>.
    /// U¿ywa Git Commits API (searchCriteria.fromDate) na repozytorium podpieraj¹cym wiki.
    /// Zwraca <c>null</c> jako sygna³ do fallbacku na pe³n¹ synchronizacjê — mo¿e wyst¹piæ gdy
    /// <c>repositoryId</c> nie zosta³o zwrócone przez API lub PAT nie ma uprawnieñ Repos (Read).
    /// </summary>
    Task<List<string>?> GetChangedWikiPagePathsAsync(WikiInfo wiki, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>
    /// Wyszukuje work itemy u¿ywaj¹c natywnego DevOps Work Item Search API
    /// (Lucene query syntax — fuzzy, wildcards, field-scoped, boolean ops, BM25).
    /// </summary>
    /// <param name="searchText">Query w sk³adni Lucene (np. "authentication~" lub "title:login AND state:Active").</param>
    /// <param name="filters">Filtry per pole DevOps. Klucze: System.WorkItemType, System.State, System.AreaPath, System.AssignedTo, System.Tags.</param>
    /// <param name="top">Maks. liczba wyników (1-1000).</param>
    /// <param name="skip">Offset paginacji.</param>
    Task<List<WorkItemSearchHit>> SearchWorkItemsByKeywordsAsync(
        string searchText,
        IReadOnlyDictionary<string, string[]>? filters = null,
        int top = 50,
        int skip = 0,
        CancellationToken ct = default);

    // ?? Repos / Pull Requests ???????????????????????????????????????????????

    /// <summary>Zwraca listê wszystkich repozytoriów Git w projekcie.</summary>
    Task<List<GitRepositoryInfo>> GetRepositoriesAsync(CancellationToken ct = default);

    /// <summary>Pobiera szczegó³y PR po jego ID i nazwie/ID repozytorium.</summary>
    Task<PullRequestDetail?> GetPullRequestAsync(string repositoryId, int pullRequestId, CancellationToken ct = default);

    /// <summary>Lista zmian plikowych w PR (add/edit/delete/rename) wzglêdem branch'a docelowego.</summary>
    Task<List<PullRequestChange>> GetPullRequestChangesAsync(string repositoryId, int pullRequestId, CancellationToken ct = default);

    /// <summary>Work itemy powi¹zane z PR (linki do work items w opisie / commitach).</summary>
    Task<List<int>> GetPullRequestWorkItemIdsAsync(string repositoryId, int pullRequestId, CancellationToken ct = default);

    /// <summary>Pobiera zawartoœæ pliku z repo na konkretnym commicie (null jeœli plik usuniêty/nieobecny).</summary>
    Task<string?> GetFileContentAsync(string repositoryId, string path, string? commitId = null, CancellationToken ct = default);

    /// <summary>Lista plików/folderów w repo na zadanej œcie¿ce (rekursywnie jeœli <paramref name="recursive"/>).</summary>
    Task<List<GitItem>> GetRepositoryItemsAsync(string repositoryId, string path = "/", bool recursive = false, string? commitId = null, CancellationToken ct = default);

    // ?? WIKI write ??????????????????????????????????????????????????????????

    /// <summary>
    /// Tworzy now¹ stronê wiki lub aktualizuje istniej¹c¹ (PUT /wiki/wikis/{wikiId}/pages).
    /// Jeœli strona istnieje wymagany jest <paramref name="eTag"/> (If-Match). Gdy <paramref name="eTag"/>
    /// jest null, robi best-effort: spróbuje pobraæ aktualny ETag i ponownie zapisaæ.
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
    /// Search API (extension) jest hostowany pod osobnym subdomenem almsearch.dev.azure.com,
    /// wiêc nie mo¿emy u¿yæ HttpClient.BaseAddress — wysy³amy z absolutnym URI.
    /// PAT z domyœlnego nag³ówka Authorization dzia³a te¿ na tym hoœcie.
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
        // Komentarze nie s¹ zwracane w $expand=all — wymagaj¹ osobnego endpointu (preview).
        var url = $"{_project}/_apis/wit/workItems/{workItemId}/comments" +
                  $"?api-version={_apiVersion}-preview.4";

        var response = await _http.GetAsync(url, ct);

        // Brak komentarzy lub starszy projekt bez wsparcia › traktujemy jako pust¹ listê
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
        // Endpoint extension Search jest na osobnym hoœcie — u¿ywamy absolutnego URI.
        var url = new Uri(
            _almSearchBaseUri,
            $"{_project}/_apis/search/workitemsearchresults?api-version={_apiVersion}");

        // DevOps Search wymaga zawsze filtra System.TeamProject — bez niego API zwraca 400.
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

        // Search extension mo¿e byæ wy³¹czone w organizacji on-prem — traktujemy jako brak wyników.
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

        // Strony-kontenery (foldery) mog¹ nie mieæ treœci › 404 to nie b³¹d
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new WikiPageDetail { Path = pagePath };

        response.EnsureSuccessStatusCode();

        // ETag z nag³ówka HTTP — unikalny token wersji strony
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
            // repositoryId nie zosta³o zwrócone przez API — fallback do pe³nej synchronizacji
            return null;
        }

        // Git Commits API z fromDate — zwraca commity od podanej daty
        var fromDate = Uri.EscapeDataString(since.UtcDateTime.ToString("o"));
        var url = $"{_project}/_apis/git/repositories/{wiki.RepositoryId}/commits" +
                  $"?searchCriteria.fromDate={fromDate}&searchCriteria.itemPath=/&$top=1000&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);

        // Brak repozytorium lub brak uprawnieñ › fallback do pe³nej synchronizacji
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        var commits = json?["value"]?.AsArray().OfType<JsonObject>().ToList() ?? [];

        if (commits.Count == 0)
            return [];

        // Dla ka¿dego commitu pobieramy zmienione pliki
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

                // Pliki wiki maj¹ rozszerzenie .md; konwertujemy œcie¿kê Git › œcie¿kê wiki
                // (usuwamy mappedPath prefix i rozszerzenie .md)
                var wikiPath = GitPathToWikiPath(itemPath, wiki.MappedPath);
                if (wikiPath is not null)
                    changedPaths.Add(wikiPath);
            }
        }

        return [.. changedPaths];
    }

    /// <summary>
    /// Konwertuje œcie¿kê pliku Git na œcie¿kê wiki.
    /// Np. "/wiki/Architecture/Overview.md" (mappedPath="/wiki") › "/Architecture/Overview".
    /// </summary>
    private static string? GitPathToWikiPath(string gitPath, string? mappedPath)
    {
        if (!gitPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return null;

        var prefix = string.IsNullOrEmpty(mappedPath) ? string.Empty : mappedPath.TrimEnd('/');
        var relative = gitPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? gitPath[prefix.Length..]
            : gitPath;

        // Usuñ rozszerzenie .md i zamieñ spacje-jako-myœlniki jeœli DevOps tak koduje
        var wikiPath = relative[..^3]; // usuñ ".md"
        return string.IsNullOrEmpty(wikiPath) ? null : wikiPath;
    }

    public async Task<List<string>> GetWikiPagePathsAsync(string wikiId, CancellationToken ct = default)
    {
        // recursionLevel=full zwraca pe³ne drzewo stron w jednym ¿¹daniu
        var url = $"{_project}/_apis/wiki/wikis/{wikiId}/pages" +
                  $"?path=/&recursionLevel=full&includeContent=false&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);

        var paths = new List<string>();
        CollectPagePaths(json, paths);
        return paths;
    }

    /// <summary>Rekurencyjnie sp³aszcza drzewo stron wiki do listy œcie¿ek.</summary>
    private static void CollectPagePaths(JsonNode? node, List<string> paths)
    {
        if (node is null) return;

        var path = node["path"]?.GetValue<string>();

        // Pomijamy root "/" — to wirtualny kontener, nie strona z treœci¹
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
        // Iteracje PR — bierzemy najnowsz¹ i z niej listê zmian (PR Changes API).
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
                OriginalPath = c["originalPath"]?.ToString()
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
        // includeContent=true zwraca treœæ w JSON-ie. Dla wersji binarnej trzeba by u¿yæ
        // $format=octetStream — tu zak³adamy pliki tekstowe (kod, markdown).
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

    // ?? WIKI write ??????????????????????????????????????????????????????????

    public async Task<WikiPageWriteResult> CreateOrUpdateWikiPageAsync(
        string wikiId, string pagePath, string contentMarkdown,
        string? eTag = null, CancellationToken ct = default)
    {
        // PUT /wiki/wikis/{wikiId}/pages?path={path} z opcjonalnym If-Match (ETag).
        // Brak If-Match ? tworzy now¹ stronê (zwraca 201). Gdy strona istnieje:
        // wymagany If-Match. Jeœli ETag null a strona istnieje, najpierw pobieramy
        // aktualny ETag i ponawiamy zapis (race-free best-effort).
        if (eTag is null)
        {
            var current = await GetWikiPageAsync(wikiId, pagePath, ct);
            if (current.Id is not null)
                eTag = current.ETag;
        }

        var result = await PutWikiPageAsync(wikiId, pagePath, contentMarkdown, eTag, ct);

        // Konflikt ETagu (412 Precondition Failed) — strona zmieni³a siê miêdzy GET a PUT.
        // Pobieramy œwie¿y ETag i ponawiamy raz.
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

        // 412 = niezgodny ETag — sygna³ do retry z odœwie¿onym ETagiem.
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
    }
}