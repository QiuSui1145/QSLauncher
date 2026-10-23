using System.Text.Json.Serialization;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed class ModrinthService
{
    public const string ApiBase = "https://api.modrinth.com/v2/";

    private readonly HttpService _http;

    public ModrinthService(HttpService http)
    {
        _http = http;
    }

    public async Task<ModrinthSearchResult> SearchAsync(
        string query,
        string projectType = "modpack",
        int limit = 20,
        int offset = 0,
        string? gameVersion = null,
        string? loader = null,
        string? category = null,
        string index = "relevance",
        CancellationToken cancellationToken = default)
    {
        var facetsList = new List<string> { $"[\"project_type:{projectType}\"]" };
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            facetsList.Add($"[\"versions:{gameVersion}\"]");
        }

        if (!string.IsNullOrWhiteSpace(loader))
        {
            facetsList.Add($"[\"categories:{loader}\"]");
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            facetsList.Add($"[\"categories:{category}\"]");
        }

        var facets = Uri.EscapeDataString("[" + string.Join(',', facetsList) + "]");
        var url = $"{ApiBase}search?query={Uri.EscapeDataString(query)}&facets={facets}" +
                  $"&limit={Math.Clamp(limit, 1, 100)}&offset={Math.Max(0, offset)}" +
                  $"&index={Uri.EscapeDataString(index)}";
        return await _http.GetJsonAsync<ModrinthSearchResult>(new Uri(url), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
        string projectId,
        string? gameVersion = null,
        string? loader = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            query.Add($"game_versions={Uri.EscapeDataString($"[\"{gameVersion}\"]")}");
        }

        if (!string.IsNullOrWhiteSpace(loader))
        {
            query.Add($"loaders={Uri.EscapeDataString($"[\"{loader}\"]")}");
        }

        var suffix = query.Count > 0 ? "?" + string.Join('&', query) : string.Empty;
        var url = $"{ApiBase}project/{Uri.EscapeDataString(projectId)}/version{suffix}";
        return await _http.GetJsonAsync<List<ModrinthVersion>>(new Uri(url), cancellationToken)
                   .ConfigureAwait(false)
               ?? [];
    }

    public async Task<ModrinthVersion> GetVersionAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{ApiBase}version/{Uri.EscapeDataString(versionId)}";
        return await _http.GetJsonAsync<ModrinthVersion>(new Uri(url), cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException("Modrinth 返回了空的版本信息。");
    }

    public async Task<ModrinthProject> GetProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{ApiBase}project/{Uri.EscapeDataString(projectId)}";
        return await _http.GetJsonAsync<ModrinthProject>(new Uri(url), cancellationToken)
            .ConfigureAwait(false);
    }

    public Task DownloadAsync(
        ModrinthFile file,
        string destination,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _http.DownloadFileAsync(
            new Uri(file.Url),
            destination,
            file.Hashes?.Sha1,
            file.Hashes?.Sha512,
            file.Size,
            progress,
            file.Filename,
            cancellationToken);
    }

    public async Task<byte[]> DownloadBytesAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        return await _http.GetBytesAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ModrinthSearchResult
{
    [JsonPropertyName("hits")]
    public List<ModrinthSearchHit> Hits { get; set; } = [];

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("total_hits")]
    public int TotalHits { get; set; }
}

public sealed class ModrinthSearchHit
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("follows")]
    public long Follows { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; } = [];

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];

    [JsonPropertyName("project_type")]
    public string ProjectType { get; set; } = string.Empty;
}

public sealed class ModrinthProject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }
}

public sealed class ModrinthVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = string.Empty;

    [JsonPropertyName("version_type")]
    public string VersionType { get; set; } = string.Empty;

    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("loaders")]
    public List<string> Loaders { get; set; } = [];

    [JsonPropertyName("files")]
    public List<ModrinthFile> Files { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public List<ModrinthDependency> Dependencies { get; set; } = [];

    [JsonPropertyName("date_published")]
    public DateTimeOffset? DatePublished { get; set; }
}

public sealed class ModrinthFile
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("hashes")]
    public ModrinthHashes? Hashes { get; set; }
}

public sealed class ModrinthHashes
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("sha512")]
    public string? Sha512 { get; set; }
}

public sealed class ModrinthDependency
{
    [JsonPropertyName("version_id")]
    public string? VersionId { get; set; }

    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("dependency_type")]
    public string DependencyType { get; set; } = string.Empty;
}
