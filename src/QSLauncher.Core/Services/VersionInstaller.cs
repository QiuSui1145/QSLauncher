using System.IO.Compression;
using System.Text.Json;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed class VersionInstaller
{
    private readonly PortablePaths _paths;
    private readonly HttpService _http;
    private readonly MojangService _mojang;
    private readonly InstanceService _instances;

    public VersionInstaller(
        PortablePaths paths,
        HttpService http,
        MojangService mojang,
        InstanceService instances)
    {
        _paths = paths;
        _http = http;
        _mojang = mojang;
        _instances = instances;
    }

    public async Task<ResolvedVersion> InstallAsync(
        InstanceInfo instance,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var version = await _mojang.ResolveAsync(instance.Manifest.VersionId, cancellationToken)
            .ConfigureAwait(false);
        await EnsureRuntimeFilesAsync(instance, version, includeAssets: true, progress, cancellationToken)
            .ConfigureAwait(false);
        return version;
    }

    public async Task EnsureRuntimeFilesAsync(
        InstanceInfo instance,
        ResolvedVersion version,
        bool includeAssets,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _instances.EnsureGameLayout(instance.Directory);
        Directory.CreateDirectory(instance.NativesDirectory);
        Directory.CreateDirectory(_paths.LibrariesPath);
        Directory.CreateDirectory(_paths.AssetsPath);

        var totalBytes = 0L;
        var totalItems = 0;
        var files = new List<DownloadSpec>();

        if (version.Client is not null && !string.IsNullOrWhiteSpace(version.Client.Url))
        {
            var clientPath = Path.Combine(_paths.VersionsPath, version.BaseId + ".jar");
            files.Add(new DownloadSpec(
                version.Client.Url,
                clientPath,
                version.Client.Sha1,
                version.Client.Size,
                $"客户端 {version.BaseId}",
                null,
                false));
            totalBytes += version.Client.Size;
            totalItems++;
        }

        var natives = new List<NativeSpec>();
        foreach (var library in version.Libraries)
        {
            if (!MojangService.RulesAllow(library.Rules))
            {
                continue;
            }

            var classifier = GetNativeClassifier(library);
            if (!string.IsNullOrWhiteSpace(classifier))
            {
                var download = library.Downloads?.Classifiers?.GetValueOrDefault(classifier);
                if (download is not null)
                {
                    var path = string.IsNullOrWhiteSpace(download.Path)
                        ? GetClassifierPath(library.Name, classifier)
                        : download.Path;
                    var url = !string.IsNullOrWhiteSpace(download.Url)
                        ? download.Url
                        : MojangService.CombineUrl(library.Url ?? MojangService.LibrariesBaseUrl, path);
                    var destination = Path.Combine(
                        _paths.LibrariesPath,
                        path.Replace('/', Path.DirectorySeparatorChar));
                    files.Add(new DownloadSpec(
                        url,
                        destination,
                        download.Sha1,
                        download.Size,
                        $"运行库 {library.Name}",
                        null,
                        true));
                    totalBytes += download.Size;
                    totalItems++;
                    natives.Add(new NativeSpec(destination, library.Extract?.Exclude));
                }
            }

            if (library.Downloads?.Artifact is { } artifact)
            {
                var relative = string.IsNullOrWhiteSpace(artifact.Path)
                    ? MojangService.GetMavenPath(library.Name)
                    : artifact.Path;
                var url = !string.IsNullOrWhiteSpace(artifact.Url)
                    ? artifact.Url
                    : MojangService.CombineUrl(library.Url ?? MojangService.LibrariesBaseUrl, relative);
                var destination = Path.Combine(
                    _paths.LibrariesPath,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                files.Add(new DownloadSpec(
                    url,
                    destination,
                    artifact.Sha1,
                    artifact.Size,
                    $"运行库 {Path.GetFileName(relative)}",
                    null,
                    false));
                totalBytes += artifact.Size;
                totalItems++;
            }
            else if (string.IsNullOrWhiteSpace(classifier)
                     && !string.IsNullOrWhiteSpace(library.Name))
            {
                var relative = MojangService.GetMavenPath(library.Name);
                var destination = Path.Combine(
                    _paths.LibrariesPath,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                files.Add(new DownloadSpec(
                    MojangService.GetMavenUrl(library),
                    destination,
                    null,
                    0,
                    $"运行库 {Path.GetFileName(relative)}",
                    null,
                    false));
                totalItems++;
            }
        }

        var assetFiles = new List<DownloadSpec>();
        if (includeAssets && version.AssetIndex is { } assetIndex
            && !string.IsNullOrWhiteSpace(assetIndex.Url))
        {
            var indexDirectory = Path.Combine(_paths.AssetsPath, "indexes");
            Directory.CreateDirectory(indexDirectory);
            var indexDestination = Path.Combine(indexDirectory, assetIndex.Id + ".json");
            await _http.DownloadFileAsync(
                new Uri(assetIndex.Url),
                indexDestination,
                assetIndex.Sha1,
                null,
                assetIndex.Size,
                progress,
                $"资源索引 {assetIndex.Id}",
                cancellationToken).ConfigureAwait(false);

            assetFiles = await BuildAssetListAsync(indexDestination, cancellationToken).ConfigureAwait(false);
            totalBytes += assetFiles.Sum(f => f.Size);
            totalItems += assetFiles.Count;
        }

        if (version.Logging?.Client?.File is { } logFile
            && !string.IsNullOrWhiteSpace(logFile.Url))
        {
            var logDirectory = Path.Combine(_paths.MetaPath, "logging");
            Directory.CreateDirectory(logDirectory);
            var logPath = GetLoggingPath(_paths, logFile);
            await _http.DownloadFileAsync(
                new Uri(logFile.Url),
                logPath,
                logFile.Sha1,
                null,
                logFile.Size,
                progress,
                "日志配置",
                cancellationToken).ConfigureAwait(false);
        }

        var aggregate = new AggregateProgress(totalBytes, totalItems, progress);
        foreach (var spec in files)
        {
            await DownloadOneAsync(spec, aggregate, cancellationToken).ConfigureAwait(false);
        }

        // Assets are many small immutable objects. Fetching a handful in
        // parallel makes a cold install dramatically faster while keeping
        // the library/client downloads ordered and easy to diagnose.
        var assets = assetFiles
            .GroupBy(f => f.Destination, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        await Parallel.ForEachAsync(
                assets,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 6,
                    CancellationToken = cancellationToken
                },
                async (spec, token) =>
                {
                    await DownloadOneAsync(spec, aggregate, token).ConfigureAwait(false);
                })
            .ConfigureAwait(false);

        foreach (var native in natives)
        {
            if (File.Exists(native.Path))
            {
                ExtractNatives(native, instance.NativesDirectory);
            }
        }

        progress?.Report(new DownloadProgress(
            "install",
            "游戏文件已就绪",
            aggregate.TotalBytes,
            aggregate.TotalBytes,
            aggregate.TotalItems,
            aggregate.TotalItems));
    }

    private async Task DownloadOneAsync(
        DownloadSpec spec,
        AggregateProgress aggregate,
        CancellationToken cancellationToken)
    {
        if (!spec.IsNative
            && File.Exists(spec.Destination)
            && await HashUtil.MatchesAsync(spec.Destination, spec.Sha1, cancellationToken: cancellationToken)
                .ConfigureAwait(false))
        {
            aggregate.Advance(spec.Size);
            return;
        }

        long received = 0;
        var fileProgress = new InlineProgress<DownloadProgress>(report =>
        {
            var delta = report.BytesReceived - received;
            if (delta > 0)
            {
                received = report.BytesReceived;
                aggregate.AddBytes(delta, report.Item);
            }
        });

        var official = new Uri(spec.Url);

        await _http.DownloadFileAsync(
            official,
            spec.Destination,
            spec.Sha1,
            null,
            spec.Size,
            fileProgress,
            spec.Label,
            cancellationToken).ConfigureAwait(false);
        aggregate.CompleteItem();
    }

    /// <summary>
    /// Prepares a linked instance's native folder from an external .minecraft
    /// root. The external files are read in place; only DLLs are copied so the
    /// isolated instance can start without touching the other installation.
    /// </summary>
    public void PrepareExternalNatives(
        ResolvedVersion version,
        string sourceRoot,
        string librariesRoot,
        string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var candidates = new[]
        {
            Path.Combine(sourceRoot, "versions", version.Id, "natives"),
            Path.Combine(sourceRoot, "natives")
        };
        foreach (var candidate in candidates)
        {
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(candidate, "*.dll"))
            {
                File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)),
                    overwrite: true);
            }

            if (Directory.EnumerateFiles(destinationDirectory, "*.dll").Any())
            {
                return;
            }
        }

        foreach (var library in version.Libraries)
        {
            if (!MojangService.RulesAllow(library.Rules))
            {
                continue;
            }

            string? relative = null;
            if (library.Downloads?.Classifiers is { } classifiers)
            {
                var native = classifiers.FirstOrDefault(pair =>
                    pair.Key.Contains("natives-windows", StringComparison.OrdinalIgnoreCase));
                relative = native.Value?.Path;
            }

            if (string.IsNullOrWhiteSpace(relative))
            {
                var classifier = GetNativeClassifier(library);
                if (!string.IsNullOrWhiteSpace(classifier))
                {
                    relative = GetClassifierPath(library.Name, classifier);
                }
            }

            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var jar = Path.Combine(librariesRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(jar))
            {
                ExtractNatives(new NativeSpec(jar, library.Extract?.Exclude), destinationDirectory);
            }
        }
    }

    public static string? GetNativeClassifier(LibraryInfo library)
    {
        if (library.Natives is null || library.Natives.Count == 0)
        {
            return null;
        }

        if (!library.Natives.TryGetValue(MojangService.CurrentOsName, out var classifier)
            || string.IsNullOrWhiteSpace(classifier))
        {
            return null;
        }

        return classifier.Replace("${arch}", MojangService.CurrentOsArch == "x86" ? "32" : "64");
    }

    private static string GetClassifierPath(string name, string classifier)
    {
        var basePath = MojangService.GetMavenPath(name);
        var directory = Path.GetDirectoryName(basePath) ?? string.Empty;
        var extension = Path.GetExtension(basePath);
        var fileName = Path.GetFileNameWithoutExtension(basePath);
        return Path.Combine(directory, $"{fileName}-{classifier}{extension}")
            .Replace('\\', '/');
    }

    public static string? GetLoggingArgument(ResolvedVersion version, PortablePaths paths)
    {
        if (version.Logging?.Client?.File is not { } file
            || string.IsNullOrWhiteSpace(version.Logging.Client.Argument))
        {
            return null;
        }

        var path = GetLoggingPath(paths, file);
        return File.Exists(path)
            ? version.Logging.Client.Argument.Replace("${path}", path)
            : null;
    }

    private static string GetLoggingPath(PortablePaths paths, LoggingFile file)
    {
        var name = Path.HasExtension(file.Id) ? file.Id : file.Id + ".xml";
        return Path.Combine(paths.MetaPath, "logging", name);
    }

    private static async Task<List<DownloadSpec>> BuildAssetListAsync(
        string indexPath,
        CancellationToken cancellationToken)
    {
        var result = new List<DownloadSpec>();
        await using var stream = File.OpenRead(indexPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("objects", out var objects)
            || objects.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var asset in objects.EnumerateObject())
        {
            if (!asset.Value.TryGetProperty("hash", out var hashElement))
            {
                continue;
            }

            var hash = hashElement.GetString();
            if (string.IsNullOrWhiteSpace(hash) || hash.Length < 2)
            {
                continue;
            }

            var size = asset.Value.TryGetProperty("size", out var sizeElement)
                ? sizeElement.GetInt64()
                : 0;
            var relative = Path.Combine(hash[..2], hash);
            var destination = Path.Combine(
                Path.Combine(Path.GetDirectoryName(indexPath)!, "..", "objects"),
                relative);
            result.Add(new DownloadSpec(
                $"https://resources.download.minecraft.net/{hash[..2]}/{hash}",
                Path.GetFullPath(destination),
                hash,
                size,
                $"资源 {Path.GetFileName(asset.Name)}",
                null,
                false));
        }

        return result;
    }

    private static void ExtractNatives(NativeSpec native, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var root = Path.GetFullPath(destinationDirectory);
        try
        {
            using var archive = ZipFile.OpenRead(native.Path);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (native.Exclude is not null && native.Exclude.Any(pattern =>
                        entry.FullName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(root, relative));
                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Warn($"解压运行库失败：{native.Path} · {ex.Message}");
        }
    }

    private sealed record DownloadSpec(
        string Url,
        string Destination,
        string? Sha1,
        long Size,
        string Label,
        string? Sha256,
        bool IsNative);

    private sealed record NativeSpec(string Path, List<string>? Exclude);

    private sealed class AggregateProgress
    {
        private readonly IProgress<DownloadProgress>? _progress;
        private long _completed;
        private int _completedItems;

        public AggregateProgress(long totalBytes, int totalItems, IProgress<DownloadProgress>? progress)
        {
            TotalBytes = totalBytes;
            TotalItems = totalItems;
            _progress = progress;
        }

        public long TotalBytes { get; }

        public int TotalItems { get; }

        public long Completed => Interlocked.Read(ref _completed);

        public void AddBytes(long bytes, string item)
        {
            if (bytes <= 0)
            {
                return;
            }

            Interlocked.Add(ref _completed, bytes);
            _progress?.Report(new DownloadProgress(
                "install",
                item,
                Completed,
                TotalBytes,
                Volatile.Read(ref _completedItems),
                TotalItems));
        }

        public void Advance(long size)
        {
            if (size > 0)
            {
                Interlocked.Add(ref _completed, size);
            }

            CompleteItem();
        }

        public void CompleteItem()
        {
            var items = Interlocked.Increment(ref _completedItems);
            _progress?.Report(new DownloadProgress(
                "install",
                "下载游戏文件",
                Completed,
                TotalBytes,
                items,
                TotalItems));
        }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;

        public InlineProgress(Action<T> action)
        {
            _action = action;
        }

        public void Report(T value) => _action(value);
    }
}
