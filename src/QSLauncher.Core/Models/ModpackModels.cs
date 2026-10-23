using System.Text.Json.Serialization;

namespace QSLauncher.Core.Models;

public sealed class ModpackIndex
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    [JsonPropertyName("game")]
    public string Game { get; set; } = string.Empty;

    [JsonPropertyName("versionId")]
    public string VersionId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("files")]
    public List<ModpackFile> Files { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string> Dependencies { get; set; } = [];
}

public sealed class ModpackFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("hashes")]
    public ModpackHashes? Hashes { get; set; }

    [JsonPropertyName("env")]
    public ModpackEnvironment? Environment { get; set; }

    [JsonPropertyName("downloads")]
    public List<string> Downloads { get; set; } = [];

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }
}

public sealed class CurseForgeManifest
{
    [JsonPropertyName("minecraft")]
    public CurseForgeMinecraft? Minecraft { get; set; }

    [JsonPropertyName("manifestType")]
    public string? ManifestType { get; set; }

    [JsonPropertyName("manifestVersion")]
    public int ManifestVersion { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("files")]
    public List<CurseForgeFile> Files { get; set; } = [];

    [JsonPropertyName("overrides")]
    public string? Overrides { get; set; }
}

public sealed class CurseForgeMinecraft
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("modLoaders")]
    public List<CurseForgeLoader> ModLoaders { get; set; } = [];
}

public sealed class CurseForgeLoader
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

public sealed class CurseForgeFile
{
    [JsonPropertyName("projectID")]
    public int ProjectId { get; set; }

    [JsonPropertyName("fileID")]
    public int FileId { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }
}

public sealed class PrismPack
{
    [JsonPropertyName("components")]
    public List<PrismComponent> Components { get; set; } = [];
}

public sealed class PrismComponent
{
    [JsonPropertyName("uid")]
    public string Uid { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("important")]
    public bool Important { get; set; }
}

public sealed class ModpackHashes
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("sha512")]
    public string? Sha512 { get; set; }
}

public sealed class ModpackEnvironment
{
    [JsonPropertyName("client")]
    public string? Client { get; set; }

    [JsonPropertyName("server")]
    public string? Server { get; set; }
}
