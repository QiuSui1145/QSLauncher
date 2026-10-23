using System.Text.Json;
using System.Text.RegularExpressions;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed record ExternalVersionInfo(
    string RootPath,
    string Id,
    string Name,
    string MinecraftVersion,
    LoaderKind Loader,
    string Type,
    DateTimeOffset? ReleasedAt,
    bool HasClientJar);

/// <summary>
/// Reads versions from user-selected .minecraft directories without copying
/// them. Registered roots are a convenience only; scanning is always explicit
/// for the root the user picked.
/// </summary>
public sealed class GameRootService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex MinecraftVersionPattern = new(
        @"^\d+\.\d+(?:\.\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        // Accept both the .minecraft folder and its parent.
        if (!Directory.Exists(Path.Combine(full, "versions"))
            && Directory.Exists(Path.Combine(full, ".minecraft", "versions")))
        {
            full = Path.Combine(full, ".minecraft");
        }

        if (!Directory.Exists(Path.Combine(full, "versions")))
        {
            throw new InvalidOperationException("该目录下没有 versions 文件夹，无法作为 .minecraft 根目录。");
        }

        return full;
    }

    public async Task<IReadOnlyList<ExternalVersionInfo>> ScanAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var root = Normalize(rootPath);
        var versionsDirectory = Path.Combine(root, "versions");
        var result = new List<ExternalVersionInfo>();
        foreach (var directory in Directory.EnumerateDirectories(versionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(directory);
            var json = Path.Combine(directory, id + ".json");
            if (!File.Exists(json))
            {
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(json);
                var info = await JsonSerializer.DeserializeAsync<VersionInfo>(
                    stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (info is null)
                {
                    continue;
                }

                var versionId = string.IsNullOrWhiteSpace(info.Id) ? id : info.Id;
                var baseId = string.IsNullOrWhiteSpace(info.InheritsFrom)
                    ? versionId
                    : info.InheritsFrom!;
                var loader = DetectLoader(
                    versionId,
                    info.InheritsFrom,
                    info.MainClass,
                    info.Libraries?.Select(library => library.Name));
                var minecraftVersion = DetectMinecraftVersion(
                    versionId, baseId, info.ClientVersion);
                var jarPath = Path.Combine(directory, versionId + ".jar");
                result.Add(new ExternalVersionInfo(
                    root,
                    versionId,
                    string.IsNullOrWhiteSpace(info.Id) ? id : info.Id,
                    minecraftVersion,
                    loader,
                    string.IsNullOrWhiteSpace(info.Type) ? "release" : info.Type,
                    info.ReleaseTime,
                    File.Exists(jarPath)));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                AppLog.Warn($"读取挂载版本失败：{json} · {ex.Message}");
            }
        }

        return result
            .OrderByDescending(v => v.ReleasedAt ?? DateTimeOffset.MinValue)
            .ThenBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string DetectMinecraftVersion(
        string versionId,
        string baseId,
        string? clientVersion = null)
    {
        if (!string.IsNullOrWhiteSpace(clientVersion)
            && MinecraftVersionPattern.IsMatch(clientVersion))
        {
            return clientVersion;
        }

        if (!string.Equals(versionId, baseId, StringComparison.OrdinalIgnoreCase)
            && MinecraftVersionPattern.IsMatch(baseId))
        {
            return baseId;
        }

        var match = MinecraftVersionPattern.Match(versionId);
        return match.Success ? match.Value : baseId;
    }

    public static LoaderKind DetectLoader(
        string versionId,
        string? inheritsFrom,
        string? mainClass = null,
        IEnumerable<string>? libraryNames = null)
    {
        var libraries = libraryNames?.ToArray() ?? [];
        var text = versionId + " " + inheritsFrom + " " + mainClass;
        bool HasLibrary(string value) => libraries.Any(
            name => name.Contains(value, StringComparison.OrdinalIgnoreCase));

        // Custom modpack version folders often use a display name that has no
        // loader word at all. Their JSON still carries the loader main class
        // and Maven libraries, so detect both before falling back to Vanilla.
        if (text.Contains("neoforge", StringComparison.OrdinalIgnoreCase)
            || HasLibrary("net.neoforged"))
        {
            return LoaderKind.NeoForge;
        }

        if (text.Contains("forge", StringComparison.OrdinalIgnoreCase)
            || text.Contains("launchwrapper", StringComparison.OrdinalIgnoreCase)
            || HasLibrary("net.minecraftforge")
            || HasLibrary("cpw.mods:modlauncher")
            || HasLibrary("cpw.mods:securejarhandler"))
        {
            return LoaderKind.Forge;
        }

        if (text.Contains("quilt", StringComparison.OrdinalIgnoreCase)
            || HasLibrary("org.quiltmc:quilt-loader"))
        {
            return LoaderKind.Quilt;
        }

        if (text.Contains("fabric", StringComparison.OrdinalIgnoreCase)
            || HasLibrary("net.fabricmc:fabric-loader"))
        {
            return LoaderKind.Fabric;
        }

        return LoaderKind.Vanilla;
    }
}
