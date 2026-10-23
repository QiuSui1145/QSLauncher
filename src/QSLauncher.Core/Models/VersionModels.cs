using System.Text.Json;
using System.Text.Json.Serialization;

namespace QSLauncher.Core.Models;

public sealed class VersionManifest
{
    [JsonPropertyName("latest")]
    public LatestVersions Latest { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<VersionSummary> Versions { get; set; } = [];
}

public sealed class LatestVersions
{
    [JsonPropertyName("release")]
    public string Release { get; set; } = string.Empty;

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; set; } = string.Empty;
}

public sealed class VersionSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    [JsonConverter(typeof(FlexibleDateTimeOffsetConverter))]
    public DateTimeOffset? Time { get; set; }

    [JsonPropertyName("releaseTime")]
    [JsonConverter(typeof(FlexibleDateTimeOffsetConverter))]
    public DateTimeOffset? ReleaseTime { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("complianceLevel")]
    public int ComplianceLevel { get; set; }
}

public sealed class VersionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("mainClass")]
    public string? MainClass { get; set; }

    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    /// <summary>
    /// Present in many PCL/curseforge-style version JSONs where the id is a
    /// display name rather than the numeric Minecraft version.
    /// </summary>
    [JsonPropertyName("clientVersion")]
    public string? ClientVersion { get; set; }

    [JsonPropertyName("assets")]
    public string? Assets { get; set; }

    [JsonPropertyName("assetIndex")]
    public AssetIndexInfo? AssetIndex { get; set; }

    [JsonPropertyName("downloads")]
    public VersionDownloads? Downloads { get; set; }

    [JsonPropertyName("javaVersion")]
    public JavaVersionRequirement? JavaVersion { get; set; }

    [JsonPropertyName("libraries")]
    public List<LibraryInfo>? Libraries { get; set; }

    [JsonPropertyName("arguments")]
    public VersionArguments? Arguments { get; set; }

    [JsonPropertyName("minecraftArguments")]
    public string? LegacyMinecraftArguments { get; set; }

    [JsonPropertyName("logging")]
    public LoggingInfo? Logging { get; set; }

    [JsonPropertyName("releaseTime")]
    [JsonConverter(typeof(FlexibleDateTimeOffsetConverter))]
    public DateTimeOffset? ReleaseTime { get; set; }

    [JsonPropertyName("time")]
    [JsonConverter(typeof(FlexibleDateTimeOffsetConverter))]
    public DateTimeOffset? Time { get; set; }

    [JsonPropertyName("complianceLevel")]
    public int ComplianceLevel { get; set; }
}

public sealed class AssetIndexInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("totalSize")]
    public long TotalSize { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public sealed class VersionDownloads
{
    [JsonPropertyName("client")]
    public DownloadInfo? Client { get; set; }

    [JsonPropertyName("server")]
    public DownloadInfo? Server { get; set; }

    [JsonPropertyName("client_mappings")]
    public DownloadInfo? ClientMappings { get; set; }
}

public sealed class DownloadInfo
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public sealed class JavaVersionRequirement
{
    [JsonPropertyName("component")]
    public string? Component { get; set; }

    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; set; }
}

public sealed class LibraryInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public LibraryDownloads? Downloads { get; set; }

    [JsonPropertyName("natives")]
    public Dictionary<string, string>? Natives { get; set; }

    [JsonPropertyName("rules")]
    public List<RuleInfo>? Rules { get; set; }

    [JsonPropertyName("extract")]
    public ExtractInfo? Extract { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    public override string ToString() => Name;
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")]
    public DownloadInfo? Artifact { get; set; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, DownloadInfo>? Classifiers { get; set; }
}

public sealed class ExtractInfo
{
    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; set; }
}

public sealed class RuleInfo
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "allow";

    [JsonPropertyName("os")]
    public OsRule? Os { get; set; }

    [JsonPropertyName("features")]
    public Dictionary<string, bool>? Features { get; set; }
}

public sealed class OsRule
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arch")]
    public string? Arch { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public sealed class VersionArguments
{
    [JsonPropertyName("game")]
    public List<JsonElement>? Game { get; set; }

    [JsonPropertyName("jvm")]
    public List<JsonElement>? Jvm { get; set; }
}

public sealed class LoggingInfo
{
    [JsonPropertyName("client")]
    public LoggingClient? Client { get; set; }
}

public sealed class LoggingClient
{
    [JsonPropertyName("argument")]
    public string? Argument { get; set; }

    [JsonPropertyName("file")]
    public LoggingFile? File { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public sealed class LoggingFile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public sealed class ArgumentEntry
{
    public ArgumentEntry(IReadOnlyList<RuleInfo>? rules, IReadOnlyList<string> values)
    {
        Rules = rules ?? [];
        Values = values;
    }

    public IReadOnlyList<RuleInfo> Rules { get; }

    public IReadOnlyList<string> Values { get; }
}

public sealed class ResolvedVersion
{
    public string Id { get; init; } = string.Empty;

    public string BaseId { get; init; } = string.Empty;

    public string Type { get; init; } = "release";

    public string MainClass { get; init; } = string.Empty;

    public string Assets { get; init; } = "legacy";

    public AssetIndexInfo? AssetIndex { get; init; }

    public DownloadInfo? Client { get; init; }

    public JavaVersionRequirement? JavaVersion { get; init; }

    public IReadOnlyList<LibraryInfo> Libraries { get; init; } = [];

    public IReadOnlyList<ArgumentEntry> GameArguments { get; init; } = [];

    public IReadOnlyList<ArgumentEntry> JvmArguments { get; init; } = [];

    public string? LegacyMinecraftArguments { get; init; }

    public LoggingInfo? Logging { get; init; }

    public DateTimeOffset? ReleaseTime { get; init; }
}
