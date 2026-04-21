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
}

public class AzureDevOpsService : IAzureDevOpsService
{
    private readonly HttpClient _http;
    private readonly string _organization;
    private readonly string _project;
    private readonly string _apiVersion = "7.1";

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
                RemoteUrl = w["remoteUrl"]?.ToString()
            })
            .Where(w => w.Id is not null)
            .ToList() ?? [];
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