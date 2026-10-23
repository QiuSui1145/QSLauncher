using System.Text.Json.Serialization;

namespace QSLauncher.Core.Models;

public enum LoaderKind
{
    Vanilla,
    Fabric,
    Quilt,
    Forge,
    NeoForge
}

public sealed class InstanceManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("versionId")]
    public string VersionId { get; set; } = string.Empty;

    [JsonPropertyName("minecraftVersion")]
    public string MinecraftVersion { get; set; } = string.Empty;

    [JsonPropertyName("loader")]
    public LoaderKind Loader { get; set; } = LoaderKind.Vanilla;

    [JsonPropertyName("loaderVersion")]
    public string? LoaderVersion { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("lastPlayedAt")]
    public DateTimeOffset? LastPlayedAt { get; set; }

    [JsonPropertyName("javaPath")]
    public string? JavaPath { get; set; }

    [JsonPropertyName("javaRuntimeId")]
    public string? JavaRuntimeId { get; set; }

    [JsonPropertyName("memoryMb")]
    public int MemoryMb { get; set; } = 4096;

    [JsonPropertyName("jvmArgs")]
    public string JvmArgs { get; set; } = string.Empty;

    [JsonPropertyName("gameArgs")]
    public string GameArgs { get; set; } = string.Empty;

    [JsonPropertyName("modpackName")]
    public string? ModpackName { get; set; }

    [JsonPropertyName("modpackVersion")]
    public string? ModpackVersion { get; set; }

    [JsonPropertyName("modpackProjectId")]
    public string? ModpackProjectId { get; set; }

    [JsonPropertyName("iconPath")]
    public string? IconPath { get; set; }

    /// <summary>
    /// Set when this instance points at a user-selected .minecraft directory.
    /// Files are used in place: the external root is the game directory and
    /// no version, mod or save is copied into the launcher's instance folder.
    /// </summary>
    [JsonPropertyName("sourceRoot")]
    public string? SourceRoot { get; set; }

    [JsonPropertyName("sourceVersionId")]
    public string? SourceVersionId { get; set; }
}

public sealed class InstanceInfo
{
    public required string Directory { get; init; }

    public required InstanceManifest Manifest { get; init; }

    private static readonly string[] IsolationMarkers =
    [
        "mods", "config", "defaultconfigs", "resourcepacks", "shaderpacks",
        "saves", "options.txt", "servers.dat", "kubejs"
    ];

    /// <summary>
    /// A managed instance keeps its own game folder. An instance linked to an
    /// external .minecraft root must use the version's isolated folder when it
    /// contains game data; otherwise Forge/Fabric would start with the shared
    /// root's empty mods/config folders and the modpack would appear vanilla.
    /// </summary>
    public string GameDirectory
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Manifest.SourceRoot))
            {
                return Path.Combine(Directory, "game");
            }

            var root = Manifest.SourceRoot;
            var versionId = Manifest.SourceVersionId ?? Manifest.VersionId;
            if (string.IsNullOrWhiteSpace(versionId))
            {
                return root;
            }

            try
            {
                var rootPath = Path.GetFullPath(root);
                var versionPath = Path.GetFullPath(Path.Combine(rootPath, "versions", versionId));
                if (versionPath.StartsWith(
                        rootPath + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)
                    && HasIsolatedGameContent(versionPath))
                {
                    return versionPath;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                           or PathTooLongException or IOException
                                           or UnauthorizedAccessException)
            {
                // Fall back to the root when a broken version id cannot be resolved.
            }

            return root;
        }
    }

    public string ModsDirectory => Path.Combine(GameDirectory, "mods");

    private static bool HasIsolatedGameContent(string versionDirectory)
    {
        if (!System.IO.Directory.Exists(versionDirectory))
        {
            return false;
        }

        return IsolationMarkers.Any(marker =>
            File.Exists(Path.Combine(versionDirectory, marker))
            || System.IO.Directory.Exists(Path.Combine(versionDirectory, marker)));
    }

    public string NativesDirectory => Path.Combine(Directory, "natives");

    public string InstanceJsonPath => Path.Combine(Directory, "instance.json");

    public string DisplayName => string.IsNullOrWhiteSpace(Manifest.Name) ? Manifest.Id : Manifest.Name;

    public override string ToString() => DisplayName;
}

public enum AccountKind
{
    Offline,
    Microsoft,
    Yggdrasil
}

public sealed class GameAccount
{
    public required string Name { get; init; }

    public required string Uuid { get; init; }

    public string AccessToken { get; init; } = "0";

    public string UserType { get; init; } = "legacy";

    public string? Xuid { get; init; }

    public string? ClientId { get; init; }

    public string UserProperties { get; init; } = "{}";

    public AccountKind Kind { get; init; } = AccountKind.Offline;

    /// <summary>Yggdrasil API root, for example https://littleskin.cn/api/yggdrasil.</summary>
    public string? ServerUrl { get; init; }

    public string? ClientToken { get; init; }

    /// <summary>Microsoft OAuth refresh token; never written to logs.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Approximate expiry of the Minecraft access token.</summary>
    public DateTimeOffset? AccessTokenExpiresAt { get; init; }

    public static GameAccount Offline(string name)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
        return new GameAccount
        {
            Name = normalized,
            Uuid = OfflineUuid(normalized),
            AccessToken = "0",
            UserType = "legacy",
            Kind = AccountKind.Offline
        };
    }

    public static string OfflineUuid(string name)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"OfflinePlayer:{name}"));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }
}
