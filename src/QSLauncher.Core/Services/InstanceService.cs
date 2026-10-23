using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed partial class InstanceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly PortablePaths _paths;

    public InstanceService(PortablePaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<InstanceInfo>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.InstancesPath);
        var result = new List<InstanceInfo>();
        foreach (var directory in Directory.EnumerateDirectories(_paths.InstancesPath))
        {
            if (Path.GetFileName(directory).StartsWith('.'))
            {
                continue;
            }

            var instance = await TryLoadAsync(directory, cancellationToken).ConfigureAwait(false);
            if (instance is not null)
            {
                await RefreshLinkedMetadataAsync(instance, cancellationToken).ConfigureAwait(false);
                result.Add(instance);
            }
        }

        return result
            .OrderByDescending(i => i.Manifest.LastPlayedAt ?? i.Manifest.CreatedAt)
            .ToList();
    }

    public async Task<InstanceInfo?> TryLoadAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(directory, "instance.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<InstanceManifest>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
            {
                return null;
            }

            return new InstanceInfo
            {
                Directory = Path.GetFullPath(directory),
                Manifest = manifest
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"实例读取失败：{directory} · {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Older linked manifests were written before loader detection inspected
    /// the version JSON's main class and libraries. Refresh those fields so an
    /// imported Forge modpack is no longer labelled as Vanilla and its mod
    /// search/instance selection uses the correct loader.
    /// </summary>
    private async Task RefreshLinkedMetadataAsync(
        InstanceInfo instance,
        CancellationToken cancellationToken)
    {
        var manifest = instance.Manifest;
        if (string.IsNullOrWhiteSpace(manifest.SourceRoot))
        {
            return;
        }

        var versionId = string.IsNullOrWhiteSpace(manifest.SourceVersionId)
            ? manifest.VersionId
            : manifest.SourceVersionId;
        if (string.IsNullOrWhiteSpace(versionId))
        {
            return;
        }

        try
        {
            var json = Path.Combine(
                manifest.SourceRoot, "versions", versionId, versionId + ".json");
            if (!File.Exists(json))
            {
                return;
            }

            await using var stream = File.OpenRead(json);
            var info = await JsonSerializer.DeserializeAsync<VersionInfo>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (info is null)
            {
                return;
            }

            var baseId = string.IsNullOrWhiteSpace(info.InheritsFrom)
                ? versionId
                : info.InheritsFrom;
            var loader = GameRootService.DetectLoader(
                versionId,
                info.InheritsFrom,
                info.MainClass,
                info.Libraries?.Select(library => library.Name));
            var minecraftVersion = GameRootService.DetectMinecraftVersion(
                versionId, baseId, info.ClientVersion);
            var changed = false;
            if (manifest.Loader != loader)
            {
                manifest.Loader = loader;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(minecraftVersion)
                && !string.Equals(manifest.MinecraftVersion, minecraftVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                manifest.MinecraftVersion = minecraftVersion;
                changed = true;
            }

            if (changed)
            {
                await SaveAsync(instance, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Warn($"刷新挂载实例元数据失败：{manifest.Id} · {ex.Message}");
        }
    }

    public async Task<InstanceInfo> CreateAsync(
        string name,
        string versionId,
        string minecraftVersion,
        LoaderKind loader = LoaderKind.Vanilla,
        string? loaderVersion = null,
        string? modpackName = null,
        string? modpackVersion = null,
        string? modpackProjectId = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.InstancesPath);
        var displayName = string.IsNullOrWhiteSpace(name) ? "未命名实例" : name.Trim();
        var id = CreateUniqueId(displayName);
        var directory = Path.Combine(_paths.InstancesPath, id);
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "game"));
        EnsureGameLayout(directory);

        var manifest = new InstanceManifest
        {
            Id = id,
            Name = displayName,
            VersionId = versionId,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            CreatedAt = DateTimeOffset.Now,
            ModpackName = modpackName,
            ModpackVersion = modpackVersion,
            ModpackProjectId = modpackProjectId
        };

        var instance = new InstanceInfo { Directory = directory, Manifest = manifest };
        await SaveAsync(instance, cancellationToken).ConfigureAwait(false);
        return instance;
    }

    public async Task<InstanceInfo> CreateLinkedAsync(
        string name,
        ExternalVersionInfo version,
        CancellationToken cancellationToken = default)
    {
        var instance = await CreateAsync(
                string.IsNullOrWhiteSpace(name) ? version.Id : name,
                version.Id,
                version.MinecraftVersion,
                version.Loader,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        instance.Manifest.SourceRoot = version.RootPath;
        instance.Manifest.SourceVersionId = version.Id;
        await SaveAsync(instance, cancellationToken).ConfigureAwait(false);
        return instance;
    }

    public async Task SaveAsync(InstanceInfo instance, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(instance.Directory);
        var temporary = instance.InstanceJsonPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                             temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                             16 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, instance.Manifest, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporary, instance.InstanceJsonPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public async Task<InstanceInfo> CopyAsync(
        InstanceInfo source,
        string? name = null,
        bool includeSaves = true,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.InstancesPath);
        var displayName = string.IsNullOrWhiteSpace(name)
            ? $"{source.DisplayName} 副本"
            : name.Trim();
        var id = CreateUniqueId(displayName);
        var directory = Path.Combine(_paths.InstancesPath, id);
        Directory.CreateDirectory(directory);

        await Task.Run(() =>
        {
            CopyDirectory(source.Directory, directory, string.Empty, includeSaves,
                cancellationToken);
        }, cancellationToken).ConfigureAwait(false);
        EnsureGameLayout(directory);

        var manifest = JsonSerializer.Deserialize<InstanceManifest>(
            JsonSerializer.Serialize(source.Manifest, JsonOptions), JsonOptions)
            ?? new InstanceManifest();
        manifest.Id = id;
        manifest.Name = displayName;
        manifest.CreatedAt = DateTimeOffset.Now;
        manifest.LastPlayedAt = null;

        var instance = new InstanceInfo { Directory = directory, Manifest = manifest };
        await SaveAsync(instance, cancellationToken).ConfigureAwait(false);
        return instance;
    }

    private static void CopyDirectory(
        string source,
        string destination,
        string parentRelative,
        bool includeSaves,
        CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(
                     source, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(entry);
            if (name.Equals("instance.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals("natives", StringComparison.OrdinalIgnoreCase)
                || name.Equals("logs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = string.IsNullOrEmpty(parentRelative)
                ? name
                : Path.Combine(parentRelative, name);
            if (!includeSaves
                && relative.Replace('\\', '/').Equals("game/saves", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.Combine(destination, name);
            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(target);
                CopyDirectory(entry, target, relative, includeSaves, cancellationToken);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(entry, target, overwrite: false);
            }
        }
    }

    public void EnsureGameLayout(string instanceDirectory)
    {
        var game = Path.Combine(instanceDirectory, "game");
        foreach (var directory in new[]
                 {
                     game,
                     Path.Combine(game, "mods"),
                     Path.Combine(game, "config"),
                     Path.Combine(game, "saves"),
                     Path.Combine(game, "resourcepacks"),
                     Path.Combine(game, "shaderpacks"),
                     Path.Combine(game, "logs"),
                     Path.Combine(instanceDirectory, "natives")
                 })
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task QuarantineAsync(
        InstanceInfo instance,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targetRoot = Path.Combine(_paths.QuarantinePath, "instances");
        Directory.CreateDirectory(targetRoot);
        var target = Path.Combine(
            targetRoot,
            $"{instance.Manifest.Id}-{DateTime.Now:yyyyMMddHHmmss}");

        try
        {
            Directory.Move(instance.Directory, target);
        }
        catch (IOException)
        {
            await Task.Run(() => Directory.Delete(instance.Directory, recursive: true), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public string GetInstanceDirectory(string id)
    {
        var safe = new string(id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(safe))
        {
            throw new InvalidOperationException("实例 ID 无效。");
        }

        var directory = Path.GetFullPath(Path.Combine(_paths.InstancesPath, safe));
        if (!directory.StartsWith(
                Path.GetFullPath(_paths.InstancesPath) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("实例路径越界。");
        }

        return directory;
    }

    private string CreateUniqueId(string displayName)
    {
        var baseName = SlugRegex().Replace(displayName.ToLowerInvariant(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "instance";
        }

        if (baseName.Length > 28)
        {
            baseName = baseName[..28].Trim('-');
        }

        var candidate = baseName;
        var counter = 2;
        while (Directory.Exists(Path.Combine(_paths.InstancesPath, candidate)))
        {
            candidate = $"{baseName}-{counter++}";
        }

        return candidate;
    }

    [GeneratedRegex("[^a-z0-9\\-]+", RegexOptions.Compiled)]
    private static partial Regex SlugRegex();
}
