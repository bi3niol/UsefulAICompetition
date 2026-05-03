using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Models;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Tools.Shared;

public interface IAzureDevOpsService
{
    Task<WorkItemDetail> GetWorkItemAsync(int id, CancellationToken ct = default);
    Task<string> AddCommentAsync(int workItemId, string comment, CancellationToken ct = default);
    Task<WikiPageDetail> GetWikiPageAsync(string wikiId, string pagePath, CancellationToken ct = default);

    /// <summary>Wykonuje zapytanie WIQL i zwraca listę ID work itemów.</summary>
    Task<List<int>> QueryWorkItemIdsAsync(string wiql, CancellationToken ct = default);

    /// <summary>Pobiera szczegóły wielu work itemów w jednym żądaniu (max 200 ID).</summary>
    Task<List<WorkItemDetail>> GetWorkItemsBatchAsync(IEnumerable<int> ids, CancellationToken ct = default);

    /// <summary>Pobiera komentarze dla pojedynczego work itemu (osobny endpoint w DevOps API).</summary>
    Task<List<WorkItemComment>> GetWorkItemCommentsAsync(int workItemId, CancellationToken ct = default);

    /// <summary>Zwraca listę wszystkich wiki w projekcie.</summary>
    Task<List<WikiInfo>> GetWikiListAsync(CancellationToken ct = default);

    /// <summary>Zwraca pełną, spłaszczoną listę ścieżek stron wiki (bez treści).</summary>
    Task<List<string>> GetWikiPagePathsAsync(string wikiId, CancellationToken ct = default);

    /// <summary>
    /// Zwraca ścieżki stron wiki zmodyfikowanych lub dodanych po <paramref name="since"/>.
    /// Używa Git Commits API (searchCriteria.fromDate) na repozytorium podpierającym wiki.
    /// Zwraca <c>null</c> jako sygnał do fallbacku na pełną synchronizację — może wystąpić gdy
    /// <c>repositoryId</c> nie zostało zwrócone przez API lub PAT nie ma uprawnień Repos (Read).
    /// </summary>
    Task<List<string>?> GetChangedWikiPagePathsAsync(WikiInfo wiki, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>
    /// Wyszukuje work itemy używając natywnego DevOps Work Item Search API
    /// (Lucene query syntax — fuzzy, wildcards, field-scoped, boolean ops, BM25).
    /// </summary>
    /// <param name="searchText">Query w składni Lucene (np. "authentication~" lub "title:login AND state:Active").</param>
    /// <param name="filters">Filtry per pole DevOps. Klucze: System.WorkItemType, System.State, System.AreaPath, System.AssignedTo, System.Tags.</param>
    /// <param name="top">Maks. liczba wyników (1-1000).</param>
    /// <param name="skip">Offset paginacji.</param>
    Task<List<WorkItemSearchHit>> SearchWorkItemsByKeywordsAsync(
        string searchText,
        IReadOnlyDictionary<string, string[]>? filters = null,
        int top = 50,
        int skip = 0,
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
    /// więc nie możemy użyć HttpClient.BaseAddress — wysyłamy z absolutnym URI.
    /// PAT z domyślnego nagłówka Authorization działa też na tym hoście.
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

    // ── Work Items ──────────────────────────────────────────────────────────

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
        // Komentarze nie są zwracane w $expand=all — wymagają osobnego endpointu (preview).
        var url = $"{_project}/_apis/wit/workItems/{workItemId}/comments" +
                  $"?api-version={_apiVersion}-preview.4";

        var response = await _http.GetAsync(url, ct);

        // Brak komentarzy lub starszy projekt bez wsparcia → traktujemy jako pustą listę
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

    // ── WIQL ────────────────────────────────────────────────────────────────

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

    // ── Work Item Search (Lucene/BM25, almsearch.dev.azure.com) ─────────────

    public async Task<List<WorkItemSearchHit>> SearchWorkItemsByKeywordsAsync(
        string searchText,
        IReadOnlyDictionary<string, string[]>? filters = null,
        int top = 50,
        int skip = 0,
        CancellationToken ct = default)
    {
        // Endpoint extension Search jest na osobnym hoście — używamy absolutnego URI.
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

        // Search extension może być wyłączone w organizacji on-prem — traktujemy jako brak wyników.
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

    // ── WIKI ────────────────────────────────────────────────────────────────

    public async Task<WikiPageDetail> GetWikiPageAsync(
        string wikiId, string pagePath, CancellationToken ct = default)
    {
        var encodedPath = Uri.EscapeDataString(pagePath);
        var url = $"{_project}/_apis/wiki/wikis/{wikiId}/pages" +
                  $"?path={encodedPath}&includeContent=true&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);

        // Strony-kontenery (foldery) mogą nie mieć treści → 404 to nie błąd
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new WikiPageDetail { Path = pagePath };

        response.EnsureSuccessStatusCode();

        // ETag z nagłówka HTTP — unikalny token wersji strony
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
            // repositoryId nie zostało zwrócone przez API — fallback do pełnej synchronizacji
            return null;
        }

        // Git Commits API z fromDate — zwraca commity od podanej daty
        var fromDate = Uri.EscapeDataString(since.UtcDateTime.ToString("o"));
        var url = $"{_project}/_apis/git/repositories/{wiki.RepositoryId}/commits" +
                  $"?searchCriteria.fromDate={fromDate}&searchCriteria.itemPath=/&$top=1000&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);

        // Brak repozytorium lub brak uprawnień → fallback do pełnej synchronizacji
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        var commits = json?["value"]?.AsArray().OfType<JsonObject>().ToList() ?? [];

        if (commits.Count == 0)
            return [];

        // Dla każdego commitu pobieramy zmienione pliki
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

                // Pliki wiki mają rozszerzenie .md; konwertujemy ścieżkę Git → ścieżkę wiki
                // (usuwamy mappedPath prefix i rozszerzenie .md)
                var wikiPath = GitPathToWikiPath(itemPath, wiki.MappedPath);
                if (wikiPath is not null)
                    changedPaths.Add(wikiPath);
            }
        }

        return [.. changedPaths];
    }

    /// <summary>
    /// Konwertuje ścieżkę pliku Git na ścieżkę wiki.
    /// Np. "/wiki/Architecture/Overview.md" (mappedPath="/wiki") → "/Architecture/Overview".
    /// </summary>
    private static string? GitPathToWikiPath(string gitPath, string? mappedPath)
    {
        if (!gitPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return null;

        var prefix = string.IsNullOrEmpty(mappedPath) ? string.Empty : mappedPath.TrimEnd('/');
        var relative = gitPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? gitPath[prefix.Length..]
            : gitPath;

        // Usuń rozszerzenie .md i zamień spacje-jako-myślniki jeśli DevOps tak koduje
        var wikiPath = relative[..^3]; // usuń ".md"
        return string.IsNullOrEmpty(wikiPath) ? null : wikiPath;
    }

    public async Task<List<string>> GetWikiPagePathsAsync(string wikiId, CancellationToken ct = default)
    {
        // recursionLevel=full zwraca pełne drzewo stron w jednym żądaniu
        var url = $"{_project}/_apis/wiki/wikis/{wikiId}/pages" +
                  $"?path=/&recursionLevel=full&includeContent=false&api-version={_apiVersion}";

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);

        var paths = new List<string>();
        CollectPagePaths(json, paths);
        return paths;
    }

    /// <summary>Rekurencyjnie spłaszcza drzewo stron wiki do listy ścieżek.</summary>
    private static void CollectPagePaths(JsonNode? node, List<string> paths)
    {
        if (node is null) return;

        var path = node["path"]?.GetValue<string>();

        // Pomijamy root "/" — to wirtualny kontener, nie strona z treścią
        if (path is not null && path != "/")
            paths.Add(path);

        var subPages = node["subPages"]?.AsArray();
        if (subPages is null) return;

        foreach (var subPage in subPages)
            CollectPagePaths(subPage, paths);
    }

    // ── Mapping ─────────────────────────────────────────────────────────────

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
}