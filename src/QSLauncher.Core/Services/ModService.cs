using System.Text.Json;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed class InstalledMod
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required string BaseName { get; init; }

    public required string DisplayName { get; init; }

    public required bool IsEnabled { get; init; }

    public string? ProjectId { get; init; }

    public string? VersionId { get; init; }

    public string? VersionNumber { get; init; }

    public long Size { get; init; }

    public DateTimeOffset ModifiedAt { get; init; }
}

public sealed record ModUpdate(
    string ProjectId,
    string Name,
    string CurrentVersionId,
    string LatestVersionId,
    string LatestVersionNumber);

public sealed class ModService
{
    private const string MetadataFile = ".qs-mods.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpService _http;
    private readonly ModrinthService _modrinth;

    public ModService(HttpService http, ModrinthService modrinth)
    {
        _http = http;
        _modrinth = modrinth;
    }

    public async Task<IReadOnlyList<InstalledMod>> ScanAsync(
        InstanceInfo instance,
        CancellationToken cancellationToken = default)
    {
        var modsDirectory = instance.ModsDirectory;
        Directory.CreateDirectory(modsDirectory);
        var metadata = await ReadMetadataAsync(instance, cancellationToken).ConfigureAwait(false);
        var byFile = metadata.Mods
            .GroupBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var result = new List<InstalledMod>();
        foreach (var path in Directory.EnumerateFiles(modsDirectory)
                     .Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                 || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
        {
            var fileName = Path.GetFileName(path);
            var isEnabled = fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            var baseName = isEnabled
                ? fileName[..^4]
                : fileName[..^".jar.disabled".Length];
            var key = baseName + ".jar";
            byFile.TryGetValue(key, out var info);
            var fileInfo = new FileInfo(path);
            result.Add(new InstalledMod
            {
                FilePath = path,
                FileName = fileName,
                BaseName = baseName,
                DisplayName = string.IsNullOrWhiteSpace(info?.Name) ? baseName : info!.Name,
                IsEnabled = isEnabled,
                ProjectId = info?.ProjectId,
                VersionId = info?.VersionId,
                VersionNumber = info?.VersionNumber,
                Size = fileInfo.Length,
                ModifiedAt = fileInfo.LastWriteTime
            });
        }

        return result
            .OrderByDescending(m => m.IsEnabled)
            .ThenBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<InstalledMod> InstallAsync(
        InstanceInfo instance,
        ModrinthSearchHit project,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var installed = await InstallWithDependenciesAsync(
                instance,
                project.ProjectId,
                project.Title,
                null,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        return installed[0];
    }

    /// <summary>
    /// Installs one Modrinth project and its required dependencies. Modrinth
    /// marks Fabric API / Cloth Config and similar libraries as required; without
    /// this walk a mod would download successfully and then fail at game start.
    /// </summary>
    private async Task<List<InstalledMod>> InstallWithDependenciesAsync(
        InstanceInfo instance,
        string projectId,
        string projectTitle,
        string? pinnedVersionId,
        HashSet<string> visiting,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!visiting.Add(projectId))
        {
            return [];
        }

        var version = pinnedVersionId is { Length: > 0 }
            ? await _modrinth.GetVersionAsync(pinnedVersionId, cancellationToken).ConfigureAwait(false)
            : await FindCompatibleVersionAsync(instance, projectId, projectTitle, cancellationToken)
                .ConfigureAwait(false);
        var file = version.Files.FirstOrDefault(f =>
                       f.Primary && f.Filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                   ?? version.Files.FirstOrDefault(f =>
                       f.Filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"{projectTitle} 的版本没有 JAR 文件。");

        Directory.CreateDirectory(instance.ModsDirectory);
        var destination = Path.Combine(instance.ModsDirectory, Path.GetFileName(file.Filename));
        await _modrinth.DownloadAsync(file, destination, progress, cancellationToken)
            .ConfigureAwait(false);

        var metadata = await ReadMetadataAsync(instance, cancellationToken).ConfigureAwait(false);
        var previous = metadata.Mods.FirstOrDefault(m =>
            string.Equals(m.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
        if (previous is not null)
        {
            foreach (var oldFile in new[]
                     {
                         Path.Combine(instance.ModsDirectory, previous.FileName),
                         Path.Combine(instance.ModsDirectory, previous.FileName + ".disabled")
                     })
            {
                try
                {
                    if (File.Exists(oldFile)
                        && !string.Equals(oldFile, destination, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(oldFile);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"删除旧模组文件失败：{oldFile} · {ex.Message}");
                }
            }
        }

        metadata.Mods.RemoveAll(m =>
            string.Equals(m.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
        metadata.Mods.Add(new ModMetadata
        {
            ProjectId = projectId,
            VersionId = version.Id,
            VersionNumber = version.VersionNumber,
            FileName = Path.GetFileName(destination),
            Name = projectTitle,
            InstalledAt = DateTimeOffset.Now
        });
        await SaveMetadataAsync(instance, metadata, cancellationToken).ConfigureAwait(false);

        var results = new List<InstalledMod>
        {
            (await ScanAsync(instance, cancellationToken).ConfigureAwait(false))
            .First(m => string.Equals(m.FileName, Path.GetFileName(destination), StringComparison.OrdinalIgnoreCase))
        };

        foreach (var dependency in version.Dependencies ?? [])
        {
            if (!string.Equals(dependency.DependencyType, "required", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(dependency.ProjectId)
                || metadata.Mods.Any(m => string.Equals(
                    m.ProjectId, dependency.ProjectId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var dependencyTitle = dependency.ProjectId;
            try
            {
                var project = await _modrinth.GetProjectAsync(dependency.ProjectId, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(project.Title))
                {
                    dependencyTitle = project.Title;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"读取前置模组信息失败：{dependency.ProjectId} · {ex.Message}");
            }

            try
            {
                results.AddRange(await InstallWithDependenciesAsync(
                        instance,
                        dependency.ProjectId,
                        dependencyTitle,
                        dependency.VersionId,
                        visiting,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"已安装 {projectTitle}，但安装前置 {dependencyTitle} 失败：{ex.Message}", ex);
            }
        }

        return results;
    }

    private async Task<ModrinthVersion> FindCompatibleVersionAsync(
        InstanceInfo instance,
        string projectId,
        string projectTitle,
        CancellationToken cancellationToken)
    {
        var loader = LoaderFacet(instance.Manifest.Loader);
        var versions = await _modrinth.GetVersionsAsync(
                projectId,
                instance.Manifest.MinecraftVersion,
                loader,
                cancellationToken)
            .ConfigureAwait(false);
        return versions
                   .OrderByDescending(v => v.DatePublished ?? DateTimeOffset.MinValue)
                   .FirstOrDefault()
               ?? throw new InvalidOperationException(
                   $"{projectTitle} 没有适用于 {instance.Manifest.MinecraftVersion}" +
                   (loader is null ? " 的版本。" : $" / {loader} 的版本。"));
    }

    public async Task<InstalledMod> ImportAsync(
        InstanceInfo instance,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)
            || !sourcePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("请选择 .jar 模组文件。");
        }

        Directory.CreateDirectory(instance.ModsDirectory);
        var fileName = Path.GetFileName(sourcePath);
        var destination = Path.Combine(instance.ModsDirectory, fileName);
        var counter = 1;
        while (File.Exists(destination))
        {
            destination = Path.Combine(
                instance.ModsDirectory,
                $"{Path.GetFileNameWithoutExtension(fileName)} ({counter++}).jar");
        }

        await using var source = File.OpenRead(sourcePath);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        return (await ScanAsync(instance, cancellationToken).ConfigureAwait(false))
            .First(m => string.Equals(m.FileName, Path.GetFileName(destination), StringComparison.OrdinalIgnoreCase));
    }

    public async Task SetEnabledAsync(
        InstalledMod mod,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mod.IsEnabled == enabled)
        {
            return;
        }

        var directory = Path.GetDirectoryName(mod.FilePath)!;
        var target = Path.Combine(
            directory,
            enabled ? mod.BaseName + ".jar" : mod.BaseName + ".jar.disabled");
        if (File.Exists(target))
        {
            throw new IOException($"目标文件已存在：{Path.GetFileName(target)}");
        }

        File.Move(mod.FilePath, target);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task DeleteAsync(InstalledMod mod, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(mod.FilePath)!;
        foreach (var path in new[]
                 {
                     Path.Combine(directory, mod.BaseName + ".jar"),
                     Path.Combine(directory, mod.BaseName + ".jar.disabled")
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        var metadataPath = Path.Combine(directory, MetadataFile);
        if (!File.Exists(metadataPath))
        {
            return;
        }

        try
        {
            var metadata = await ReadMetadataAsync(directory, cancellationToken).ConfigureAwait(false);
            metadata.Mods.RemoveAll(m =>
                string.Equals(m.FileName, mod.BaseName + ".jar", StringComparison.OrdinalIgnoreCase));
            await SaveMetadataAsync(directory, metadata, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"清理模组元数据失败：{ex.Message}");
        }
    }

    public async Task<IReadOnlyList<ModUpdate>> CheckUpdatesAsync(
        InstanceInfo instance,
        CancellationToken cancellationToken = default)
    {
        var metadata = await ReadMetadataAsync(instance, cancellationToken).ConfigureAwait(false);
        var loader = LoaderFacet(instance.Manifest.Loader);
        var updates = new List<ModUpdate>();
        foreach (var mod in metadata.Mods.Where(m => !string.IsNullOrWhiteSpace(m.ProjectId)))
        {
            try
            {
                var versions = await _modrinth.GetVersionsAsync(
                        mod.ProjectId!,
                        instance.Manifest.MinecraftVersion,
                        loader,
                        cancellationToken)
                    .ConfigureAwait(false);
                var latest = versions
                    .OrderByDescending(v => v.DatePublished ?? DateTimeOffset.MinValue)
                    .FirstOrDefault();
                if (latest is not null
                    && !string.Equals(latest.Id, mod.VersionId, StringComparison.OrdinalIgnoreCase))
                {
                    updates.Add(new ModUpdate(
                        mod.ProjectId!,
                        mod.Name,
                        mod.VersionId ?? string.Empty,
                        latest.Id,
                        latest.VersionNumber));
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"检查 {mod.Name} 更新失败：{ex.Message}");
            }
        }

        return updates;
    }

    public async Task UpdateAsync(
        InstanceInfo instance,
        ModUpdate update,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var hit = new ModrinthSearchHit { ProjectId = update.ProjectId, Title = update.Name };
        var metadata = await ReadMetadataAsync(instance, cancellationToken).ConfigureAwait(false);
        var existing = metadata.Mods.FirstOrDefault(m =>
            string.Equals(m.ProjectId, update.ProjectId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var oldPath = Path.Combine(instance.ModsDirectory, existing.FileName);
            var disabledPath = oldPath + ".disabled";
            if (File.Exists(disabledPath))
            {
                oldPath = disabledPath;
            }

            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }

            metadata.Mods.Remove(existing);
            await SaveMetadataAsync(instance, metadata, cancellationToken).ConfigureAwait(false);
        }

        await InstallAsync(instance, hit, progress, cancellationToken).ConfigureAwait(false);
    }

    public static string? LoaderFacet(LoaderKind loader) => loader switch
    {
        LoaderKind.Fabric => "fabric",
        LoaderKind.Quilt => "quilt",
        LoaderKind.Forge => "forge",
        LoaderKind.NeoForge => "neoforge",
        _ => null
    };

    private static async Task<ModMetadataFile> ReadMetadataAsync(
        InstanceInfo instance,
        CancellationToken cancellationToken)
        => await ReadMetadataAsync(instance.ModsDirectory, cancellationToken).ConfigureAwait(false);

    private static async Task<ModMetadataFile> ReadMetadataAsync(
        string modsDirectory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(modsDirectory, MetadataFile);
        if (!File.Exists(path))
        {
            return new ModMetadataFile();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ModMetadataFile>(json, JsonOptions)
                   ?? new ModMetadataFile();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"读取模组元数据失败：{ex.Message}");
            return new ModMetadataFile();
        }
    }

    private static async Task SaveMetadataAsync(
        InstanceInfo instance,
        ModMetadataFile metadata,
        CancellationToken cancellationToken)
        => await SaveMetadataAsync(instance.ModsDirectory, metadata, cancellationToken).ConfigureAwait(false);

    private static async Task SaveMetadataAsync(
        string modsDirectory,
        ModMetadataFile metadata,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(modsDirectory);
        var path = Path.Combine(modsDirectory, MetadataFile);
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ModMetadataFile
{
    public List<ModMetadata> Mods { get; set; } = [];
}

public sealed class ModMetadata
{
    public string? ProjectId { get; set; }

    public string? VersionId { get; set; }

    public string? VersionNumber { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset InstalledAt { get; set; }
}
