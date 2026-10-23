using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed class ResourceCategory
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Path { get; init; }

    public long Bytes { get; init; }

    public int Files { get; init; }

    public bool CanClean { get; init; }
}

public sealed class ResourceService
{
    private readonly PortablePaths _paths;

    public ResourceService(PortablePaths paths)
    {
        _paths = paths;
    }

    public Task<IReadOnlyList<ResourceCategory>> GetStatsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<ResourceCategory>>(() =>
        {
            var categories = new[]
            {
                Measure("assets", "游戏资源", "声音、语言与贴图对象，按哈希存放。", _paths.AssetsPath),
                Measure("libraries", "依赖库", "Minecraft 与模组加载器共用的 Java 库。", _paths.LibrariesPath),
                Measure("versions", "版本元数据", "官方版本 JSON、加载器描述与日志配置。", _paths.VersionsPath),
                Measure("java", "Java 运行时", "由启动器下载的便携 Java。", _paths.JavaPath),
                Measure("instances", "实例与存档", "世界、配置、模组与整合包覆盖层。", _paths.InstancesPath),
                Measure("cache", "缓存", "可重新生成的索引与临时缓存。", _paths.CachePath, canClean: true),
                Measure("downloads", "下载暂存", "下载中断后留下的临时文件。", _paths.DownloadsPath, canClean: true),
                Measure("logs", "运行日志", "启动器与游戏输出，可安全清理。", _paths.LogsPath, canClean: true),
                Measure("backgrounds", "背景素材", "自定义图片与视频背景。", _paths.BackgroundsPath)
            };
            return categories;
        }, cancellationToken);
    }

    public async Task<int> CleanAsync(string key, CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        switch (key)
        {
            case "logs":
                deleted += await DeleteFilesAsync(_paths.LogsPath, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "cache":
                deleted += await DeleteFilesAsync(_paths.CachePath, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "downloads":
                deleted += await DeleteFilesAsync(_paths.DownloadsPath, cancellationToken)
                    .ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException("该类别不支持自动清理。");
        }

        AppLog.Info($"资源清理 {key}：删除 {deleted} 个文件。");
        return deleted;
    }

    public long GetReclaimableBytes()
    {
        try
        {
            return Measure("logs", "运行日志", string.Empty, _paths.LogsPath).Bytes;
        }
        catch
        {
            return 0;
        }
    }

    private static ResourceCategory Measure(
        string key,
        string name,
        string description,
        string path,
        bool canClean = false)
    {
        long bytes = 0;
        var files = 0;
        if (Directory.Exists(path))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             path,
                             "*",
                             new EnumerationOptions
                             {
                                 RecurseSubdirectories = true,
                                 IgnoreInaccessible = true,
                                 AttributesToSkip = FileAttributes.ReparsePoint
                             }))
                {
                    try
                    {
                        bytes += new FileInfo(file).Length;
                        files++;
                    }
                    catch
                    {
                        // A file can disappear while the page is open.
                    }
                }
            }
            catch
            {
                // Keep partial statistics.
            }
        }

        return new ResourceCategory
        {
            Key = key,
            Name = name,
            Description = description,
            Path = path,
            Bytes = bytes,
            Files = files,
            CanClean = canClean
        };
    }

    private static async Task<int> DeleteFilesAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"清理日志失败：{file} · {ex.Message}");
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return deleted;
    }

    private static async Task<int> DeletePatternAsync(
        string directory,
        string pattern,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(
                     directory,
                     pattern,
                     new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         IgnoreInaccessible = true,
                         AttributesToSkip = FileAttributes.ReparsePoint
                     }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"清理临时文件失败：{file} · {ex.Message}");
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return deleted;
    }
}
