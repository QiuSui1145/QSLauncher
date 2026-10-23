using System.IO.Compression;
using System.Text.Json;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public enum ContentKind
{
    ResourcePacks,
    ShaderPacks,
    Saves
}

public sealed class ContentItem
{
    public required ContentKind Kind { get; init; }

    public required string FullPath { get; init; }

    public required string Name { get; init; }

    public required bool IsDirectory { get; init; }

    public long Bytes { get; init; }

    public DateTimeOffset ModifiedAt { get; init; }

    public string? Detail { get; init; }

    public bool IsEnabled { get; init; }

    public string EnabledText => IsEnabled ? "已启用" : "启用";

    public string ToggleText => IsEnabled ? "停用" : "启用";

    public string ModifiedText => ModifiedAt.ToString("yyyy-MM-dd HH:mm");

    public string SizeText => Bytes >= 1024L * 1024 * 1024
        ? $"{Bytes / 1024d / 1024d / 1024d:0.00} GB"
        : Bytes >= 1024 * 1024
            ? $"{Bytes / 1024d / 1024d:0.0} MB"
            : Bytes >= 1024
                ? $"{Bytes / 1024d:0.0} KB"
                : $"{Bytes} B";
}

/// <summary>
/// Manages the per-instance content folders that are not mods: resource packs,
/// shader packs and worlds. Destructive actions move content into the portable
/// quarantine folder instead of deleting it permanently.
/// </summary>
public sealed class InstanceContentService
{
    private readonly PortablePaths _paths;

    public InstanceContentService(PortablePaths paths)
    {
        _paths = paths;
    }

    public string GetDirectory(InstanceInfo instance, ContentKind kind)
    {
        var name = kind switch
        {
            ContentKind.ResourcePacks => "resourcepacks",
            ContentKind.ShaderPacks => "shaderpacks",
            ContentKind.Saves => "saves",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return Path.Combine(instance.GameDirectory, name);
    }

    public Task<IReadOnlyList<ContentItem>> ListAsync(
        InstanceInfo instance,
        ContentKind kind,
        CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<ContentItem>>(() =>
        {
            var directory = GetDirectory(instance, kind);
            if (!Directory.Exists(directory))
            {
                return [];
            }

            var enabledPacks = kind == ContentKind.ResourcePacks
                ? ReadEnabledResourcePacks(instance)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<ContentItem>();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isDirectory = Directory.Exists(path);
                var name = Path.GetFileName(path);
                if (name.StartsWith('.') || name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (kind is ContentKind.ResourcePacks or ContentKind.ShaderPacks
                    && !isDirectory
                    && !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (kind == ContentKind.Saves
                    && !isDirectory
                    && !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var size = isDirectory ? GetDirectorySize(path) : new FileInfo(path).Length;
                var modified = isDirectory
                    ? Directory.GetLastWriteTimeUtc(path)
                    : File.GetLastWriteTimeUtc(path);
                var detail = kind switch
                {
                    ContentKind.ResourcePacks => ReadPackDetail(path),
                    ContentKind.ShaderPacks => "光影包 · 在游戏内由 Iris / OptiFine 启用",
                    _ => isDirectory ? "世界存档" : "存档压缩包"
                };

                items.Add(new ContentItem
                {
                    Kind = kind,
                    FullPath = path,
                    Name = name,
                    IsDirectory = isDirectory,
                    Bytes = size,
                    ModifiedAt = new DateTimeOffset(modified).ToLocalTime(),
                    Detail = detail,
                    IsEnabled = kind == ContentKind.ResourcePacks
                                && enabledPacks.Contains(GetPackKey(name))
                });
            }

            return items
                .OrderByDescending(i => i.ModifiedAt)
                .ToList();
        }, cancellationToken);

    public async Task<ContentItem> ImportAsync(
        InstanceInfo instance,
        ContentKind kind,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            throw new FileNotFoundException("找不到要导入的文件。", sourcePath);
        }

        var directory = GetDirectory(instance, kind);
        Directory.CreateDirectory(directory);
        var sourceName = Path.GetFileName(
            sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (kind == ContentKind.Saves && Path.GetExtension(sourcePath)
                .Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var folderName = Path.GetFileNameWithoutExtension(sourceName);
            var destination = UniquePath(directory, folderName);
            Directory.CreateDirectory(destination);
            try
            {
                await Task.Run(() =>
                {
                    using var archive = ZipFile.OpenRead(sourcePath);
                    ExtractSafely(archive, destination);
                }, cancellationToken).ConfigureAwait(false);
                if (!File.Exists(Path.Combine(destination, "level.dat")))
                {
                    throw new InvalidDataException("这个压缩包不是有效的世界存档：缺少 level.dat。");
                }
            }
            catch
            {
                if (Directory.Exists(destination))
                {
                    Directory.Delete(destination, true);
                }

                throw;
            }

            return (await ListAsync(instance, kind, cancellationToken).ConfigureAwait(false))
                .First(i => string.Equals(i.FullPath, destination, StringComparison.OrdinalIgnoreCase));
        }

        if (Directory.Exists(sourcePath))
        {
            var destination = UniquePath(directory, sourceName);
            await Task.Run(() => CopyDirectory(sourcePath, destination), cancellationToken)
                .ConfigureAwait(false);
            return (await ListAsync(instance, kind, cancellationToken).ConfigureAwait(false))
                .First(i => string.Equals(i.FullPath, destination, StringComparison.OrdinalIgnoreCase));
        }

        var target = UniqueFile(directory, sourceName);
        await Task.Run(() => File.Copy(sourcePath, target, overwrite: false), cancellationToken)
            .ConfigureAwait(false);
        return (await ListAsync(instance, kind, cancellationToken).ConfigureAwait(false))
            .First(i => string.Equals(i.FullPath, target, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> BackupSaveAsync(
        ContentItem item,
        CancellationToken cancellationToken = default)
    {
        if (item.Kind != ContentKind.Saves || !Directory.Exists(item.FullPath))
        {
            throw new InvalidOperationException("只能备份文件夹形式的世界存档。");
        }

        var directory = Path.Combine(_paths.DataRoot, "backups", "saves");
        Directory.CreateDirectory(directory);
        var safeName = string.Concat(item.Name.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var destination = UniqueFile(
            directory,
            $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        await Task.Run(() =>
        {
            ZipFile.CreateFromDirectory(
                item.FullPath,
                destination,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);
        }, cancellationToken).ConfigureAwait(false);
        AppLog.Info($"存档已备份：{destination}");
        return destination;
    }

    public async Task<string> DeleteToQuarantineAsync(
        ContentItem item,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.FullPath)
            || !(File.Exists(item.FullPath) || Directory.Exists(item.FullPath)))
        {
            throw new FileNotFoundException("内容已经不存在。", item.FullPath);
        }

        var quarantine = Path.Combine(_paths.DataRoot, "quarantine");
        Directory.CreateDirectory(quarantine);
        var destination = UniquePath(
            quarantine,
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(item.FullPath)}");
        await Task.Run(() =>
        {
            if (Directory.Exists(item.FullPath))
            {
                Directory.Move(item.FullPath, destination);
            }
            else
            {
                File.Move(item.FullPath, destination);
            }
        }, cancellationToken).ConfigureAwait(false);
        AppLog.Info($"内容已移入隔离区：{destination}");
        return destination;
    }

    public async Task SetResourcePackEnabledAsync(
        InstanceInfo instance,
        ContentItem item,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (item.Kind != ContentKind.ResourcePacks)
        {
            throw new InvalidOperationException("只有资源包支持启用或停用。");
        }

        var optionsPath = Path.Combine(instance.GameDirectory, "options.txt");
        var lines = File.Exists(optionsPath)
            ? (await File.ReadAllLinesAsync(optionsPath, cancellationToken).ConfigureAwait(false)).ToList()
            : [];
        var key = GetPackKey(item.Name);
        var index = lines.FindIndex(line =>
            line.StartsWith("resourcePacks:", StringComparison.Ordinal));
        var packs = new List<string>();
        if (index >= 0)
        {
            var raw = lines[index]["resourcePacks:".Length..].Trim();
            try
            {
                packs = JsonSerializer.Deserialize<List<string>>(raw) ?? [];
            }
            catch (JsonException)
            {
                packs = [];
            }
        }

        packs.RemoveAll(p => p.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (enabled)
        {
            packs.Add(key);
        }

        var value = "resourcePacks:" + JsonSerializer.Serialize(packs);
        if (index >= 0)
        {
            lines[index] = value;
        }
        else
        {
            lines.Add(value);
        }

        if (File.Exists(optionsPath))
        {
            File.Copy(optionsPath, optionsPath + ".qs-backup", overwrite: true);
        }

        Directory.CreateDirectory(instance.GameDirectory);
        var temporary = optionsPath + $".tmp-{Guid.NewGuid():N}";
        await File.WriteAllLinesAsync(temporary, lines, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, optionsPath, overwrite: true);
        AppLog.Info($"资源包状态已更新：{item.Name} → {(enabled ? "启用" : "停用")}");
    }

    private static string GetPackKey(string name) => "file/" + name;

    private static HashSet<string> ReadEnabledResourcePacks(InstanceInfo instance)
    {
        var optionsPath = Path.Combine(instance.GameDirectory, "options.txt");
        if (!File.Exists(optionsPath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            foreach (var line in File.ReadLines(optionsPath))
            {
                if (!line.StartsWith("resourcePacks:", StringComparison.Ordinal))
                {
                    continue;
                }

                var raw = line["resourcePacks:".Length..].Trim();
                var packs = JsonSerializer.Deserialize<List<string>>(raw) ?? [];
                return new HashSet<string>(packs, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            AppLog.Warn($"读取 options.txt 资源包列表失败：{ex.Message}");
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string ReadPackDetail(string path)
    {
        try
        {
            string? json = null;
            if (Directory.Exists(path))
            {
                var metaPath = Path.Combine(path, "pack.mcmeta");
                if (File.Exists(metaPath))
                {
                    json = File.ReadAllText(metaPath);
                }
            }
            else
            {
                using var archive = ZipFile.OpenRead(path);
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase));
                if (entry is not null)
                {
                    using var reader = new StreamReader(entry.Open());
                    json = reader.ReadToEnd();
                }
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return "资源包 · 未找到 pack.mcmeta";
            }

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("pack", out var pack))
            {
                return "资源包";
            }

            var format = pack.TryGetProperty("pack_format", out var formatElement)
                         && formatElement.TryGetInt32(out var formatValue)
                ? $"格式 {formatValue}"
                : "资源包";
            var description = pack.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.ValueKind == JsonValueKind.String
                    ? descriptionElement.GetString()
                    : descriptionElement.ToString()
                : null;
            return string.IsNullOrWhiteSpace(description)
                ? format
                : $"{format} · {description}";
        }
        catch
        {
            return "资源包 · 元数据无法读取";
        }
    }

    private static void ExtractSafely(ZipArchive archive, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("存档压缩包包含非法路径。");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch
        {
            return 0;
        }
    }

    private static string UniqueFile(string directory, string fileName)
    {
        var destination = Path.Combine(directory, fileName);
        var counter = 1;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        while (File.Exists(destination))
        {
            destination = Path.Combine(directory, $"{stem} ({counter++}){extension}");
        }

        return destination;
    }

    private static string UniquePath(string directory, string name)
    {
        var destination = Path.Combine(directory, name);
        var counter = 1;
        while (File.Exists(destination) || Directory.Exists(destination))
        {
            destination = Path.Combine(directory, $"{name} ({counter++})");
        }

        return destination;
    }
}
