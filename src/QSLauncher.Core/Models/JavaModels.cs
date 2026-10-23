using System.Text.Json;
using System.Text.Json.Serialization;

namespace QSLauncher.Core.Models;

public sealed class JavaRuntimeInfo
{
    public required string Id { get; init; }

    public required string ExecutablePath { get; init; }

    public required string VersionString { get; init; }

    public int MajorVersion { get; init; }

    public string Vendor { get; init; } = "Unknown";

    public string Architecture { get; init; } = "x64";

    public bool IsPortable { get; init; }

    public string Source { get; init; } = "system";

    public string DisplayName => $"Java {MajorVersion} · {Vendor}";

    public override string ToString() => DisplayName;
}

public sealed class JavaRuntimeIndex
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Platforms { get; set; }
}
