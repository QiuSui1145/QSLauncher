using System.Runtime.InteropServices;
using System.Text.Json;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed class MojangService
{
    public const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public const string FallbackManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";
    public const string LibrariesBaseUrl = "https://libraries.minecraft.net/";
    public const string AssetsBaseUrl = "https://resources.download.minecraft.net/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly PortablePaths _paths;
    private readonly HttpService _http;
    private readonly SemaphoreSlim _manifestGate = new(1, 1);

    public MojangService(PortablePaths paths, HttpService http)
    {
        _paths = paths;
        _http = http;
    }

    public async Task<VersionManifest> GetManifestAsync(
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        await _manifestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cachePath = Path.Combine(_paths.MetaPath, "version_manifest_v2.json");
            if (!refresh)
            {
                var cached = await TryReadAsync<VersionManifest>(cachePath, cancellationToken)
                    .ConfigureAwait(false);
                if (cached is not null && cached.Versions.Count > 0)
                {
                    return cached;
                }
            }

            VersionManifest? manifest = null;
            foreach (var url in new[] { VersionManifestUrl, FallbackManifestUrl })
            {
                try
                {
                    manifest = await _http.GetJsonAsync<VersionManifest>(
                        new Uri(url), cancellationToken).ConfigureAwait(false);
                    if (manifest.Versions.Count > 0)
                    {
                        break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Warn($"版本清单获取失败：{url} · {ex.Message}");
                }
            }

            if (manifest is null || manifest.Versions.Count == 0)
            {
                var cached = await TryReadAsync<VersionManifest>(cachePath, cancellationToken)
                    .ConfigureAwait(false);
                if (cached is not null && cached.Versions.Count > 0)
                {
                    AppLog.Warn("使用本地缓存的版本清单。");
                    return cached;
                }

                throw new IOException("无法获取 Minecraft 版本清单，请检查网络连接。");
            }

            await WriteAtomicAsync(cachePath, manifest, cancellationToken).ConfigureAwait(false);
            return manifest;
        }
        finally
        {
            _manifestGate.Release();
        }
    }

    public async Task<VersionInfo> GetVersionInfoAsync(
        VersionSummary summary,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var cachePath = Path.Combine(_paths.VersionsPath, $"{summary.Id}.json");
        if (!refresh)
        {
            var cached = await TryReadAsync<VersionInfo>(cachePath, cancellationToken)
                .ConfigureAwait(false);
            if (cached is not null)
            {
                return cached;
            }
        }

        var info = await _http.GetJsonAsync<VersionInfo>(
            new Uri(summary.Url), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(info.Id))
        {
            info.Id = summary.Id;
        }

        await WriteAtomicAsync(cachePath, info, cancellationToken).ConfigureAwait(false);
        return info;
    }

    public async Task<VersionInfo> GetVersionInfoAsync(
        string versionId,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var cachePath = Path.Combine(_paths.VersionsPath, $"{versionId}.json");
        if (!refresh)
        {
            var cached = await TryReadAsync<VersionInfo>(cachePath, cancellationToken)
                .ConfigureAwait(false);
            if (cached is not null)
            {
                return cached;
            }

            // Forge / NeoForge installers write their descriptor into a
            // version-named folder instead of the flat launcher cache.
            var nestedPath = Path.Combine(_paths.VersionsPath, versionId, $"{versionId}.json");
            var nested = await TryReadAsync<VersionInfo>(nestedPath, cancellationToken)
                .ConfigureAwait(false);
            if (nested is not null)
            {
                return nested;
            }
        }

        var manifest = await GetManifestAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var summary = manifest.Versions.FirstOrDefault(v =>
            v.Id.Equals(versionId, StringComparison.OrdinalIgnoreCase));
        if (summary is null)
        {
            throw new InvalidOperationException($"版本清单中找不到 {versionId}。");
        }

        return await GetVersionInfoAsync(summary, refresh, cancellationToken).ConfigureAwait(false);
    }

    public Task SaveVersionInfoAsync(
        VersionInfo info,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_paths.VersionsPath, $"{info.Id}.json");
        return WriteAtomicAsync(path, info, cancellationToken);
    }

    public async Task<ResolvedVersion> ResolveAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        return await ResolveCoreAsync(
                versionId,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                (id, ct) => GetVersionInfoAsync(id, cancellationToken: ct),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a version and its parents from an external .minecraft root.
    /// No network access and no files are copied.
    /// </summary>
    public async Task<ResolvedVersion> ResolveFromRootAsync(
        string rootPath,
        string versionId,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(rootPath);
        return await ResolveCoreAsync(
                versionId,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                (id, ct) => ReadRootVersionInfoAsync(root, id, ct),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<VersionInfo> ReadRootVersionInfoAsync(
        string root,
        string versionId,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, "versions", versionId, versionId + ".json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"挂载目录缺少版本文件：{path}");
        }

        await using var stream = File.OpenRead(path);
        var info = await JsonSerializer.DeserializeAsync<VersionInfo>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken)
            .ConfigureAwait(false);
        if (info is null || string.IsNullOrWhiteSpace(info.Id))
        {
            throw new InvalidOperationException($"挂载版本 JSON 无效：{path}");
        }

        return info;
    }

    private async Task<ResolvedVersion> ResolveCoreAsync(
        string versionId,
        HashSet<string> visited,
        Func<string, CancellationToken, Task<VersionInfo>> load,
        CancellationToken cancellationToken)
    {
        if (!visited.Add(versionId))
        {
            throw new InvalidOperationException($"版本继承出现循环：{versionId}");
        }

        var info = await load(versionId, cancellationToken).ConfigureAwait(false);

        ResolvedVersion? parent = null;
        if (!string.IsNullOrWhiteSpace(info.InheritsFrom))
        {
            parent = await ResolveCoreAsync(info.InheritsFrom, visited, load, cancellationToken)
                .ConfigureAwait(false);
        }

        var libraries = new List<LibraryInfo>(parent?.Libraries.Count ?? 0);
        if (parent is not null)
        {
            libraries.AddRange(parent.Libraries);
        }

        if (info.Libraries is not null)
        {
            libraries.AddRange(info.Libraries);
        }

        var gameArgs = new List<ArgumentEntry>(parent?.GameArguments.Count ?? 0);
        if (parent is not null)
        {
            gameArgs.AddRange(parent.GameArguments);
        }

        gameArgs.AddRange(ParseArguments(info.Arguments?.Game));

        var jvmArgs = new List<ArgumentEntry>(parent?.JvmArguments.Count ?? 0);
        if (parent is not null)
        {
            jvmArgs.AddRange(parent.JvmArguments);
        }

        jvmArgs.AddRange(ParseArguments(info.Arguments?.Jvm));

        return new ResolvedVersion
        {
            Id = info.Id,
            BaseId = parent?.BaseId ?? info.Id,
            Type = string.IsNullOrWhiteSpace(info.Type) ? "release" : info.Type,
            MainClass = info.MainClass ?? parent?.MainClass ?? string.Empty,
            Assets = info.Assets ?? parent?.Assets ?? "legacy",
            AssetIndex = info.AssetIndex ?? parent?.AssetIndex,
            Client = info.Downloads?.Client ?? parent?.Client,
            JavaVersion = info.JavaVersion ?? parent?.JavaVersion,
            Libraries = libraries,
            GameArguments = gameArgs,
            JvmArguments = jvmArgs,
            LegacyMinecraftArguments = info.LegacyMinecraftArguments ?? parent?.LegacyMinecraftArguments,
            Logging = info.Logging ?? parent?.Logging,
            ReleaseTime = info.ReleaseTime ?? parent?.ReleaseTime
        };
    }

    public static List<ArgumentEntry> ParseArguments(List<JsonElement>? elements)
    {
        var result = new List<ArgumentEntry>();
        if (elements is null)
        {
            return result;
        }

        foreach (var element in elements)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                result.Add(new ArgumentEntry(null, [element.GetString() ?? string.Empty]));
                continue;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var rules = element.TryGetProperty("rules", out var rulesElement)
                ? ParseRules(rulesElement)
                : null;

            var values = new List<string>();
            if (element.TryGetProperty("value", out var valueElement))
            {
                if (valueElement.ValueKind == JsonValueKind.String)
                {
                    values.Add(valueElement.GetString() ?? string.Empty);
                }
                else if (valueElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in valueElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            values.Add(item.GetString() ?? string.Empty);
                        }
                    }
                }
            }

            if (values.Count > 0)
            {
                result.Add(new ArgumentEntry(rules, values));
            }
        }

        return result;
    }

    public static IReadOnlyList<RuleInfo>? ParseRules(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var rules = new List<RuleInfo>();
        foreach (var ruleElement in element.EnumerateArray())
        {
            if (ruleElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var rule = new RuleInfo
            {
                Action = ruleElement.TryGetProperty("action", out var action)
                         && action.ValueKind == JsonValueKind.String
                    ? action.GetString() ?? "allow"
                    : "allow"
            };

            if (ruleElement.TryGetProperty("os", out var osElement) && osElement.ValueKind == JsonValueKind.Object)
            {
                rule.Os = new OsRule
                {
                    Name = osElement.TryGetProperty("name", out var name) ? name.GetString() : null,
                    Arch = osElement.TryGetProperty("arch", out var arch) ? arch.GetString() : null,
                    Version = osElement.TryGetProperty("version", out var version) ? version.GetString() : null
                };
            }

            if (ruleElement.TryGetProperty("features", out var features)
                && features.ValueKind == JsonValueKind.Object)
            {
                rule.Features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var feature in features.EnumerateObject())
                {
                    rule.Features[feature.Name] = feature.Value.ValueKind == JsonValueKind.True;
                }
            }

            rules.Add(rule);
        }

        return rules;
    }

    public static string CurrentOsName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux";

    public static string CurrentOsArch => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        _ => "x86_64"
    };

    public static bool RulesAllow(
        IReadOnlyList<RuleInfo>? rules,
        IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules)
        {
            if (!RuleMatches(rule, features))
            {
                continue;
            }

            allowed = rule.Action.Equals("allow", StringComparison.OrdinalIgnoreCase);
        }

        return allowed;
    }

    private static bool RuleMatches(RuleInfo rule, IReadOnlyDictionary<string, bool>? features)
    {
        if (rule.Os is not null)
        {
            if (!string.IsNullOrWhiteSpace(rule.Os.Name)
                && !rule.Os.Name.Equals(CurrentOsName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(rule.Os.Arch)
                && !rule.Os.Arch.Equals(CurrentOsArch, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (rule.Features is not null)
        {
            foreach (var (name, expected) in rule.Features)
            {
                var actual = features is not null && features.TryGetValue(name, out var value) && value;
                if (actual != expected)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static string GetMavenPath(string name)
    {
        var extension = "jar";
        var at = name.IndexOf('@');
        if (at >= 0)
        {
            extension = name[(at + 1)..];
            name = name[..at];
        }

        var parts = name.Split(':');
        if (parts.Length < 3)
        {
            return name.Replace(':', '/') + "." + extension;
        }

        var group = parts[0];
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;
        return $"{group.Replace('.', '/')}/{artifact}/{version}/{artifact}-{version}{classifier}.{extension}";
    }

    public static string GetMavenUrl(LibraryInfo library)
    {
        if (!string.IsNullOrWhiteSpace(library.Downloads?.Artifact?.Url))
        {
            return library.Downloads.Artifact.Url;
        }

        var relative = library.Downloads?.Artifact?.Path;
        if (string.IsNullOrWhiteSpace(relative))
        {
            relative = GetMavenPath(library.Name);
        }

        var baseUrl = string.IsNullOrWhiteSpace(library.Url) ? LibrariesBaseUrl : library.Url;
        return CombineUrl(baseUrl, relative);
    }

    public static string CombineUrl(string baseUrl, string relative)
    {
        if (Uri.TryCreate(relative, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        var normalizedBase = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        return new Uri(new Uri(normalizedBase), relative).ToString();
    }

    private static async Task<T?> TryReadAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"缓存读取失败：{path} · {ex.Message}");
            return null;
        }
    }

    private static async Task WriteAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                             temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                             16 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
