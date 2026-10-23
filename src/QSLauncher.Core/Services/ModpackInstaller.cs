using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed class ModpackInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly PortablePaths _paths;
    private readonly HttpService _http;
    private readonly InstanceService _instances;
    private readonly LoaderService _loaders;
    private readonly VersionInstaller _installer;

    public ModpackInstaller(
        PortablePaths paths,
        HttpService http,
        InstanceService instances,
        LoaderService loaders,
        VersionInstaller installer)
    {
        _paths = paths;
        _http = http;
        _instances = instances;
        _loaders = loaders;
        _installer = installer;
    }

    public async Task<InstanceInfo> InstallFromVersionAsync(
        ModrinthVersion version,
        string? projectId = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var file = version.Files.FirstOrDefault(f =>
                       f.Primary && f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                   ?? version.Files.FirstOrDefault(f =>
                       f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("该整合包版本没有 .mrpack 文件。");

        var cacheDirectory = Path.Combine(_paths.CachePath, "modpacks");
        Directory.CreateDirectory(cacheDirectory);
        var cachePath = Path.Combine(cacheDirectory, $"{version.Id}.mrpack");
        progress?.Report(new DownloadProgress("modpack", $"下载 {file.Filename}", 0, 1));
        await _http.DownloadFileAsync(
            new Uri(file.Url),
            cachePath,
            file.Hashes?.Sha1,
            file.Hashes?.Sha512,
            file.Size,
            progress,
            file.Filename,
            cancellationToken).ConfigureAwait(false);

        return await InstallFromFileAsync(
                cachePath,
                projectId ?? version.ProjectId,
                version.VersionNumber,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<InstanceInfo> InstallFromFileAsync(
        string archivePath,
        string? projectId = null,
        string? packVersion = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? fallbackMinecraftVersion = null,
        LoaderKind fallbackLoader = LoaderKind.Vanilla,
        string? fallbackLoaderVersion = null)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("找不到整合包文件。", archivePath);
        }

        var format = DetectFormat(archivePath);
        switch (format)
        {
            case ModpackFormat.CurseForge:
                return await InstallCurseForgeAsync(
                        archivePath, packVersion, progress, cancellationToken)
                    .ConfigureAwait(false);
            case ModpackFormat.Prism:
                return await InstallPrismAsync(
                        archivePath, packVersion, progress, cancellationToken)
                    .ConfigureAwait(false);
            case ModpackFormat.Generic:
                return await InstallGenericAsync(
                        archivePath, packVersion, progress, cancellationToken,
                        fallbackMinecraftVersion, fallbackLoader, fallbackLoaderVersion)
                    .ConfigureAwait(false);
        }

        using var archive = ZipFile.OpenRead(archivePath);
        var indexEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("不是有效的 Modrinth 整合包：缺少 modrinth.index.json。");

        ModpackIndex index;
        await using (var stream = indexEntry.Open())
        {
            index = await JsonSerializer.DeserializeAsync<ModpackIndex>(
                        stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("modrinth.index.json 内容为空。");
        }

        if (index.FormatVersion != 1)
        {
            throw new InvalidDataException($"不支持的整合包格式版本：{index.FormatVersion}。");
        }

        if (!index.Game.Equals("minecraft", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"不支持的整合包游戏类型：{index.Game}。");
        }

        if (!index.Dependencies.TryGetValue("minecraft", out var minecraftVersion)
            || string.IsNullOrWhiteSpace(minecraftVersion))
        {
            throw new InvalidDataException("整合包没有声明 Minecraft 版本。");
        }

        var loader = ResolveLoader(index.Dependencies, out var loaderVersion);
        var displayName = string.IsNullOrWhiteSpace(index.Name)
            ? Path.GetFileNameWithoutExtension(archivePath)
            : index.Name.Trim();

        progress?.Report(new DownloadProgress("modpack", "创建实例目录", 0, 1));
        var instance = await _instances.CreateAsync(
                displayName,
                minecraftVersion,
                minecraftVersion,
                loader,
                loaderVersion,
                displayName,
                packVersion ?? index.VersionId,
                projectId,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            EnsureDiskSpace(instance, index);

            if (loader is not LoaderKind.Vanilla)
            {
                progress?.Report(new DownloadProgress("modpack", $"安装 {loader} {loaderVersion}", 0, 1));
                await _loaders.InstallAsync(
                        instance,
                        loader,
                        loaderVersion,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(new DownloadProgress("modpack", "安装原版基础文件与依赖库", 0, 1));
            await _installer.InstallAsync(instance, progress, cancellationToken)
                .ConfigureAwait(false);

            await InstallFilesAsync(archive, index, instance, progress, cancellationToken)
                .ConfigureAwait(false);

            await ExtractOverridesAsync(archive, instance, progress, cancellationToken)
                .ConfigureAwait(false);

            instance.Manifest.ModpackName = displayName;
            instance.Manifest.ModpackVersion = packVersion ?? index.VersionId;
            instance.Manifest.ModpackProjectId = projectId;
            await _instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);

            progress?.Report(new DownloadProgress("modpack", "整合包安装完成", 1, 1));
            AppLog.Info($"整合包安装完成：{displayName}（{minecraftVersion} / {loader}）。");
            return instance;
        }
        catch
        {
            progress?.Report(new DownloadProgress("modpack", "安装失败，正在回滚", 0, 1));
            await _instances.QuarantineAsync(instance, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task InstallFilesAsync(
        ZipArchive archive,
        ModpackIndex index,
        InstanceInfo instance,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = index.Files
            .Where(f => !string.Equals(f.Environment?.Client, "unsupported", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Downloads.Count > 0 && !string.IsNullOrWhiteSpace(f.Downloads[0]))
            .ToList();

        if (files.Count == 0)
        {
            return;
        }

        var totalBytes = files.Sum(f => Math.Max(0, f.FileSize));
        long completedBytes = 0;
        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[i];
            var destination = SafeCombine(instance.GameDirectory, file.Path);
            var url = file.Downloads[0];
            var label = $"整合包文件 {i + 1}/{files.Count}";
            var baseBytes = completedBytes;
            var fileProgress = new InlineProgress<DownloadProgress>(report =>
            {
                var current = baseBytes + report.BytesReceived;
                progress?.Report(new DownloadProgress(
                    "modpack",
                    label,
                    current,
                    Math.Max(totalBytes, current)));
            });

            await _http.DownloadFileAsync(
                new Uri(url),
                destination,
                file.Hashes?.Sha1,
                file.Hashes?.Sha512,
                file.FileSize,
                fileProgress,
                label,
                cancellationToken).ConfigureAwait(false);

            completedBytes += Math.Max(0, file.FileSize);
        }

        progress?.Report(new DownloadProgress("modpack", "整合包文件已安装", totalBytes, totalBytes));
    }

    private static async Task ExtractOverridesAsync(
        ZipArchive archive,
        InstanceInfo instance,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var entries = archive.Entries
            .Where(e => e.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .ToList();

        long total = entries.Sum(e => Math.Max(0, e.Length));
        long completed = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = entry.FullName["overrides/".Length..];
            var destination = SafeCombine(instance.GameDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var input = entry.Open())
            await using (var output = new FileStream(
                             destination, FileMode.Create, FileAccess.Write, FileShare.None,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            completed += Math.Max(0, entry.Length);
            progress?.Report(new DownloadProgress("overrides", $"展开覆盖层 {relative}", completed, Math.Max(total, completed)));
        }
    }

    private static LoaderKind ResolveLoader(
        IReadOnlyDictionary<string, string> dependencies,
        out string? loaderVersion)
    {
        foreach (var key in new[] { "fabric-loader", "quilt-loader", "forge", "neoforge" })
        {
            if (dependencies.TryGetValue(key, out var version) && !string.IsNullOrWhiteSpace(version))
            {
                loaderVersion = version;
                return key switch
                {
                    "fabric-loader" => LoaderKind.Fabric,
                    "quilt-loader" => LoaderKind.Quilt,
                    "forge" => LoaderKind.Forge,
                    _ => LoaderKind.NeoForge
                };
            }
        }

        loaderVersion = null;
        return LoaderKind.Vanilla;
    }

    /// <summary>
    /// True when the archive has no format metadata and needs a base version
    /// from the user before it can be installed.
    /// </summary>
    public static bool IsGenericArchive(string archivePath)
    {
        try
        {
            return DetectFormat(archivePath) == ModpackFormat.Generic;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// True only when a plain ZIP has no readable Minecraft/loader metadata.
    /// Many PCL and hand-made packs embed version.json or PCL/Setup.ini, so
    /// those can be installed without interrupting the user.
    /// </summary>
    public static bool NeedsFallback(string archivePath)
    {
        try
        {
            if (DetectFormat(archivePath) != ModpackFormat.Generic)
            {
                return false;
            }

            using var archive = ZipFile.OpenRead(archivePath);
            return DetectGenericMetadata(archive).Version is null;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Overlays a modpack archive onto an existing instance. This is used when
    /// a ZIP is dropped on the launcher: the currently mounted game directory
    /// receives the pack's overrides without creating a second instance.
    /// </summary>
    public async Task<InstanceInfo> InstallOverlayAsync(
        string archivePath,
        InstanceInfo target,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("找不到整合包文件。", archivePath);
        }

        var format = DetectFormat(archivePath);
        using var archive = ZipFile.OpenRead(archivePath);
        var displayName = Path.GetFileNameWithoutExtension(archivePath);
        string? packVersion = null;
        string? minecraftVersion = null;
        LoaderKind loader = LoaderKind.Vanilla;
        string? loaderVersion = null;
        var missingFiles = 0;

        switch (format)
        {
            case ModpackFormat.CurseForge:
            {
                var entry = archive.Entries.First(e =>
                    e.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
                CurseForgeManifest manifest;
                await using (var stream = entry.Open())
                {
                    manifest = await JsonSerializer.DeserializeAsync<CurseForgeManifest>(
                                   stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                               ?? throw new InvalidDataException("manifest.json 内容为空。");
                }

                displayName = string.IsNullOrWhiteSpace(manifest.Name) ? displayName : manifest.Name.Trim();
                packVersion = manifest.Version;
                minecraftVersion = manifest.Minecraft?.Version;
                var loaderId = manifest.Minecraft?.ModLoaders.FirstOrDefault(l => l.Primary)?.Id
                    ?? manifest.Minecraft?.ModLoaders.FirstOrDefault()?.Id
                    ?? string.Empty;
                (loader, loaderVersion) = ParseCurseForgeLoader(loaderId);

                progress?.Report(new DownloadProgress("modpack", "展开 CurseForge 覆盖层", 0, 1));
                var prefix = string.IsNullOrWhiteSpace(manifest.Overrides)
                    ? "overrides/"
                    : manifest.Overrides.TrimEnd('/') + "/";
                await ExtractPrefixAsync(archive, target, prefix, progress, cancellationToken)
                    .ConfigureAwait(false);
                missingFiles = await DownloadCurseForgeFilesAsync(
                        manifest, target, onlyMissing: true, progress, cancellationToken)
                    .ConfigureAwait(false);
                break;
            }

            case ModpackFormat.Modrinth:
            {
                var entry = archive.Entries.First(e =>
                    e.FullName.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase));
                ModpackIndex index;
                await using (var stream = entry.Open())
                {
                    index = await JsonSerializer.DeserializeAsync<ModpackIndex>(
                                stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                            ?? throw new InvalidDataException("modrinth.index.json 内容为空。");
                }

                displayName = string.IsNullOrWhiteSpace(index.Name) ? displayName : index.Name.Trim();
                packVersion = index.VersionId;
                minecraftVersion = index.Dependencies.GetValueOrDefault("minecraft");
                loader = ResolveLoader(index.Dependencies, out loaderVersion);
                await ExtractOverridesAsync(archive, target, progress, cancellationToken)
                    .ConfigureAwait(false);
                await DownloadModrinthFilesAsync(
                        index, target, onlyMissing: true, progress, cancellationToken)
                    .ConfigureAwait(false);
                break;
            }

            case ModpackFormat.Prism:
                progress?.Report(new DownloadProgress("modpack", "展开 Prism 覆盖层", 0, 1));
                await ExtractPrefixAsync(archive, target, ".minecraft/", progress, cancellationToken)
                    .ConfigureAwait(false);
                break;

            default:
            {
                var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
                var prefix = names.Any(n => n.StartsWith(".minecraft/", StringComparison.OrdinalIgnoreCase))
                    ? ".minecraft/"
                    : string.Empty;
                var detected = DetectGenericMetadata(archive);
                minecraftVersion = detected.Version;
                loader = detected.Loader;
                loaderVersion = detected.LoaderVersion;
                progress?.Report(new DownloadProgress("modpack", "展开 ZIP 整合包", 0, 1));
                await ExtractPrefixAsync(archive, target, prefix, progress, cancellationToken)
                    .ConfigureAwait(false);
                break;
            }
        }

        target.Manifest.ModpackName = displayName;
        target.Manifest.ModpackVersion = packVersion ?? target.Manifest.ModpackVersion;
        target.Manifest.MinecraftVersion = string.IsNullOrWhiteSpace(minecraftVersion)
            ? target.Manifest.MinecraftVersion
            : minecraftVersion;
        if (loader is not LoaderKind.Vanilla)
        {
            target.Manifest.Loader = loader;
            target.Manifest.LoaderVersion = loaderVersion ?? target.Manifest.LoaderVersion;
        }

        await _instances.SaveAsync(target, cancellationToken).ConfigureAwait(false);
        AppLog.Info($"整合包已覆盖到当前实例：{displayName} -> {target.GameDirectory}");
        var detail = missingFiles > 0
            ? $"已安装到当前挂载目录；{missingFiles} 个 CurseForge 文件缺少下载地址，已保留本地文件"
            : "整合包已安装到当前挂载目录";
        progress?.Report(new DownloadProgress("modpack", detail, 1, 1));
        return target;
    }

    private async Task<int> DownloadCurseForgeFilesAsync(
        CurseForgeManifest manifest,
        InstanceInfo target,
        bool onlyMissing,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = manifest.Files
            .Where(f => f.Required)
            .Where(f => !string.IsNullOrWhiteSpace(f.DownloadUrl))
            .ToList();
        var skipped = manifest.Files.Count(f => f.Required) - files.Count;
        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[i];
            var fileName = Path.GetFileName(new Uri(file.DownloadUrl!).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"{file.ProjectId}-{file.FileId}.jar";
            }

            var destination = Path.Combine(target.ModsDirectory, fileName);
            if (onlyMissing && File.Exists(destination))
            {
                continue;
            }

            await _http.DownloadFileAsync(
                    new Uri(file.DownloadUrl!), destination, null, null, 0,
                    progress, fileName, cancellationToken)
                .ConfigureAwait(false);
        }

        if (skipped > 0)
        {
            AppLog.Warn($"整合包有 {skipped} 个 CurseForge 文件没有下载地址，已保留本地现有文件。");
            progress?.Report(new DownloadProgress(
                "modpack", $"{skipped} 个文件缺少下载地址，已保留本地模组", 1, 1));
        }

        return skipped;
    }

    private async Task DownloadModrinthFilesAsync(
        ModpackIndex index,
        InstanceInfo target,
        bool onlyMissing,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = index.Files
            .Where(f => !string.Equals(f.Environment?.Client, "unsupported", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Downloads.Count > 0 && !string.IsNullOrWhiteSpace(f.Downloads[0]))
            .ToList();
        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[i];
            var destination = SafeCombine(target.GameDirectory, file.Path);
            if (onlyMissing && File.Exists(destination))
            {
                continue;
            }

            await _http.DownloadFileAsync(
                    new Uri(file.Downloads[0]),
                    destination,
                    file.Hashes?.Sha1,
                    file.Hashes?.Sha512,
                    file.FileSize,
                    progress,
                    $"整合包文件 {i + 1}/{files.Count}",
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static (string? Version, LoaderKind Loader, string? LoaderVersion)
        DetectGenericMetadata(ZipArchive archive)
    {
        foreach (var name in new[]
                 {
                     "version.json", ".minecraft/version.json",
                     "minecraftinstance.json", ".minecraft/minecraftinstance.json"
                 })
        {
            var entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Replace('\\', '/').Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                continue;
            }

            try
            {
                using var stream = entry.Open();
                using var document = JsonDocument.Parse(stream);
                var detected = ReadJsonMetadata(document.RootElement);
                if (detected.Version is not null)
                {
                    return detected;
                }
            }
            catch (JsonException)
            {
                // Try the next metadata source.
            }
        }

        var iniEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.Replace('\\', '/').EndsWith("PCL/Setup.ini", StringComparison.OrdinalIgnoreCase)
            || e.FullName.Equals("Setup.ini", StringComparison.OrdinalIgnoreCase));
        if (iniEntry is not null)
        {
            try
            {
                using var stream = iniEntry.Open();
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (reader.ReadLine() is { } line)
                {
                    var index = line.IndexOf('=');
                    if (index > 0)
                    {
                        values[line[..index].Trim()] = line[(index + 1)..].Trim();
                    }
                }

                var version = FirstValue(values, "MinecraftVersion", "GameVersion", "Version");
                var loaderName = FirstValue(values, "Loader", "ModLoader", "LoaderType");
                var loaderVersion = FirstValue(
                    values, "LoaderVersion", "ForgeVersion", "FabricVersion", "QuiltVersion");
                if (!string.IsNullOrWhiteSpace(version))
                {
                    var parsed = TryParsePackId(version);
                    var loader = ParseLoaderName(loaderName) ?? parsed.Loader;
                    return (parsed.Version ?? version, loader, loaderVersion ?? parsed.LoaderVersion);
                }
            }
            catch (IOException)
            {
                // Ignore malformed PCL metadata.
            }
        }

        // A bundled versions/<id>/<id>.json is another common hand-made pack
        // layout. The directory name usually contains the complete pack id.
        var versionEntry = archive.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .FirstOrDefault(n => Regex.IsMatch(
                n, @"(^|/)versions/[^/]+/[^/]+\.json$", RegexOptions.IgnoreCase));
        if (versionEntry is not null)
        {
            var id = versionEntry.Split('/')[^2];
            var parsed = TryParsePackId(id);
            if (parsed.Version is not null)
            {
                return parsed;
            }
        }

        return (null, LoaderKind.Vanilla, null);
    }

    private static (string? Version, LoaderKind Loader, string? LoaderVersion)
        ReadJsonMetadata(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (null, LoaderKind.Vanilla, null);
        }

        var id = GetString(root, "id");
        var version = GetString(root, "minecraftVersion")
                      ?? GetString(root, "clientVersion")
                      ?? GetString(root, "inheritsFrom");
        var parsed = TryParsePackId(version ?? id ?? string.Empty);

        var loaderName = GetString(root, "loader") ?? GetString(root, "modLoader");
        string? loaderVersion = null;
        if (root.TryGetProperty("baseModLoader", out var baseLoader)
            && baseLoader.ValueKind == JsonValueKind.Object)
        {
            loaderName = GetString(baseLoader, "name") ?? loaderName;
            loaderVersion = GetString(baseLoader, "forgeVersion")
                            ?? GetString(baseLoader, "fabricVersion")
                            ?? GetString(baseLoader, "version");
        }

        var loader = ParseLoaderName(loaderName) ?? parsed.Loader;
        if (loader == LoaderKind.Vanilla)
        {
            loader = InferLoaderFromJson(root);
        }

        var resolvedVersion = parsed.Version ?? version;
        if (resolvedVersion is not null)
        {
            var parsedVersion = TryParsePackId(resolvedVersion);
            resolvedVersion = parsedVersion.Version ?? resolvedVersion;
            if (loader == LoaderKind.Vanilla)
            {
                loader = parsedVersion.Loader;
            }
        }

        return (resolvedVersion, loader, loaderVersion ?? parsed.LoaderVersion);
    }

    private static LoaderKind InferLoaderFromJson(JsonElement root)
    {
        var text = (GetString(root, "mainClass") ?? string.Empty).ToLowerInvariant();
        if (root.TryGetProperty("libraries", out var libraries)
            && libraries.ValueKind == JsonValueKind.Array)
        {
            foreach (var library in libraries.EnumerateArray())
            {
                text += " " + (GetString(library, "name") ?? string.Empty).ToLowerInvariant();
            }
        }

        return text switch
        {
            _ when text.Contains("neoforge", StringComparison.Ordinal) => LoaderKind.NeoForge,
            _ when text.Contains("quilt", StringComparison.Ordinal) => LoaderKind.Quilt,
            _ when text.Contains("fabric", StringComparison.Ordinal) => LoaderKind.Fabric,
            _ when text.Contains("forge", StringComparison.Ordinal) => LoaderKind.Forge,
            _ => LoaderKind.Vanilla
        };
    }

    private static (string? Version, LoaderKind Loader, string? LoaderVersion)
        TryParsePackId(string value)
    {
        var text = value.Trim();
        var match = Regex.Match(text, @"(?<!\d)(\d+\.\d+(?:\.\d+)?)(?!\d)");
        if (!match.Success)
        {
            return (null, LoaderKind.Vanilla, null);
        }

        var version = match.Value;
        var tail = text[(match.Index + match.Length)..].Trim(' ', '-', '_', '+');
        var lower = tail.ToLowerInvariant();
        LoaderKind loader;
        if (lower.Contains("neoforge", StringComparison.Ordinal))
        {
            loader = LoaderKind.NeoForge;
        }
        else if (lower.Contains("quilt", StringComparison.Ordinal))
        {
            loader = LoaderKind.Quilt;
        }
        else if (lower.Contains("fabric", StringComparison.Ordinal))
        {
            loader = LoaderKind.Fabric;
        }
        else if (lower.Contains("forge", StringComparison.Ordinal))
        {
            loader = LoaderKind.Forge;
        }
        else
        {
            return (version, LoaderKind.Vanilla, null);
        }

        var loaderVersion = Regex.Match(tail, @"\d[\w.\-+]*").Value.Trim('-', '_', '+');
        return (version, loader, string.IsNullOrWhiteSpace(loaderVersion) ? null : loaderVersion);
    }

    private static LoaderKind? ParseLoaderName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var lower = value.ToLowerInvariant();
        if (lower.Contains("neoforge", StringComparison.Ordinal))
        {
            return LoaderKind.NeoForge;
        }

        if (lower.Contains("quilt", StringComparison.Ordinal))
        {
            return LoaderKind.Quilt;
        }

        if (lower.Contains("fabric", StringComparison.Ordinal))
        {
            return LoaderKind.Fabric;
        }

        if (lower.Contains("forge", StringComparison.Ordinal))
        {
            return LoaderKind.Forge;
        }

        return lower.Contains("vanilla", StringComparison.Ordinal) ? LoaderKind.Vanilla : null;
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? FirstValue(Dictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private enum ModpackFormat
    {
        Modrinth,
        CurseForge,
        Prism,
        Generic
    }

    private static ModpackFormat DetectFormat(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        if (names.Any(n => n.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase)))
        {
            return ModpackFormat.Modrinth;
        }

        var manifestEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is not null && LooksLikeCurseForgeManifest(manifestEntry))
        {
            return ModpackFormat.CurseForge;
        }

        if (names.Any(n => n.EndsWith("mmc-pack.json", StringComparison.OrdinalIgnoreCase)))
        {
            return ModpackFormat.Prism;
        }

        return ModpackFormat.Generic;
    }

    private static bool LooksLikeCurseForgeManifest(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("minecraft", out var minecraft)
                   && minecraft.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("manifestType", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<InstanceInfo> InstallCurseForgeAsync(
        string archivePath,
        string? packVersion,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.First(e =>
            e.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        CurseForgeManifest manifest;
        await using (var stream = entry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<CurseForgeManifest>(
                           stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidDataException("manifest.json 内容为空。");
        }

        var minecraftVersion = manifest.Minecraft?.Version;
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            throw new InvalidDataException("CurseForge 整合包缺少 Minecraft 版本。");
        }

        var loaders = manifest.Minecraft!.ModLoaders ?? [];
        var loaderId = loaders.FirstOrDefault(l => l.Primary)?.Id
            ?? loaders.FirstOrDefault()?.Id
            ?? string.Empty;
        var (loader, loaderVersion) = ParseCurseForgeLoader(loaderId);
        var displayName = string.IsNullOrWhiteSpace(manifest.Name)
            ? Path.GetFileNameWithoutExtension(archivePath)
            : manifest.Name.Trim();

        progress?.Report(new DownloadProgress("modpack", "创建 CurseForge 实例", 0, 1));
        var instance = await _instances.CreateAsync(
                displayName, minecraftVersion, minecraftVersion, loader, loaderVersion,
                displayName, packVersion ?? manifest.Version, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await InstallLoaderAndBaseAsync(
                    instance, loader, loaderVersion, progress, cancellationToken)
                .ConfigureAwait(false);
            var prefix = string.IsNullOrWhiteSpace(manifest.Overrides)
                ? "overrides/"
                : manifest.Overrides.TrimEnd('/') + "/";
            await ExtractPrefixAsync(archive, instance, prefix, progress, cancellationToken)
                .ConfigureAwait(false);

            var skipped = 0;
            var downloaded = 0;
            foreach (var file in manifest.Files.Where(f => f.Required))
            {
                if (string.IsNullOrWhiteSpace(file.DownloadUrl))
                {
                    skipped++;
                    continue;
                }

                var fileName = Path.GetFileName(new Uri(file.DownloadUrl).AbsolutePath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = $"{file.ProjectId}-{file.FileId}.jar";
                }

                var destination = SafeCombine(instance.ModsDirectory, fileName);
                await _http.DownloadFileAsync(
                        new Uri(file.DownloadUrl!), destination, null, null, 0,
                        progress, fileName, cancellationToken)
                    .ConfigureAwait(false);
                downloaded++;
            }

            instance.Manifest.ModpackName = displayName;
            instance.Manifest.ModpackVersion = packVersion ?? manifest.Version;
            await _instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);
            AppLog.Info($"CurseForge 整合包已安装：{displayName}，下载 {downloaded} 个，跳过 {skipped} 个。");
            progress?.Report(new DownloadProgress(
                "modpack",
                skipped > 0 ? $"安装完成；{skipped} 个文件需在 CurseForge 客户端补全" : "安装完成",
                1, 1));
            return instance;
        }
        catch
        {
            await _instances.QuarantineAsync(instance, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<InstanceInfo> InstallPrismAsync(
        string archivePath,
        string? packVersion,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.First(e =>
            e.FullName.EndsWith("mmc-pack.json", StringComparison.OrdinalIgnoreCase));
        PrismPack pack;
        await using (var stream = entry.Open())
        {
            pack = await JsonSerializer.DeserializeAsync<PrismPack>(
                       stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidDataException("mmc-pack.json 内容为空。");
        }

        var minecraftVersion = pack.Components
            .FirstOrDefault(c => c.Uid.Equals("net.minecraft", StringComparison.OrdinalIgnoreCase))?.Version;
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            throw new InvalidDataException("Prism/MultiMC 整合包缺少 net.minecraft 组件。");
        }

        var loader = LoaderKind.Vanilla;
        string? loaderVersion = null;
        foreach (var component in pack.Components)
        {
            if (component.Uid.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase))
            {
                loader = LoaderKind.Fabric;
            }
            else if (component.Uid.Contains("quilt-loader", StringComparison.OrdinalIgnoreCase))
            {
                loader = LoaderKind.Quilt;
            }
            else if (component.Uid.Contains("neoforge", StringComparison.OrdinalIgnoreCase)
                     || component.Uid.Contains("neoforged", StringComparison.OrdinalIgnoreCase))
            {
                loader = LoaderKind.NeoForge;
            }
            else if (component.Uid.Contains("forge", StringComparison.OrdinalIgnoreCase))
            {
                loader = LoaderKind.Forge;
            }
            else
            {
                continue;
            }

            loaderVersion = component.Version;
            break;
        }

        var displayName = Path.GetFileNameWithoutExtension(archivePath);
        var instance = await _instances.CreateAsync(
                displayName, minecraftVersion, minecraftVersion, loader, loaderVersion,
                displayName, packVersion, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await InstallLoaderAndBaseAsync(
                    instance, loader, loaderVersion, progress, cancellationToken)
                .ConfigureAwait(false);
            await ExtractPrefixAsync(
                    archive, instance, ".minecraft/", progress, cancellationToken)
                .ConfigureAwait(false);
            instance.Manifest.ModpackName = displayName;
            instance.Manifest.ModpackVersion = packVersion;
            await _instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);
            progress?.Report(new DownloadProgress("modpack", "Prism 整合包安装完成", 1, 1));
            return instance;
        }
        catch
        {
            await _instances.QuarantineAsync(instance, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<InstanceInfo> InstallGenericAsync(
        string archivePath,
        string? packVersion,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken,
        string? fallbackMinecraftVersion,
        LoaderKind fallbackLoader,
        string? fallbackLoaderVersion)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (string.IsNullOrWhiteSpace(fallbackMinecraftVersion))
        {
            var detected = DetectGenericMetadata(archive);
            fallbackMinecraftVersion = detected.Version;
            fallbackLoader = detected.Loader;
            fallbackLoaderVersion = detected.LoaderVersion;
        }

        if (string.IsNullOrWhiteSpace(fallbackMinecraftVersion))
        {
            throw new InvalidDataException(
                "无法识别此整合包格式。支持 Modrinth、CurseForge、Prism/MultiMC，以及包含 version.json / PCL/Setup.ini 的 ZIP；" +
                "其他 ZIP 需要先在下载页选择 Minecraft 版本。");
        }

        var displayName = Path.GetFileNameWithoutExtension(archivePath);
        var instance = await _instances.CreateAsync(
                displayName,
                fallbackMinecraftVersion,
                fallbackMinecraftVersion,
                fallbackLoader,
                fallbackLoaderVersion,
                displayName,
                packVersion,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await InstallLoaderAndBaseAsync(
                    instance, fallbackLoader, fallbackLoaderVersion, progress, cancellationToken)
                .ConfigureAwait(false);
            var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            var prefix = names.Any(n => n.StartsWith(".minecraft/", StringComparison.OrdinalIgnoreCase))
                ? ".minecraft/"
                : string.Empty;
            await ExtractPrefixAsync(archive, instance, prefix, progress, cancellationToken)
                .ConfigureAwait(false);
            instance.Manifest.ModpackName = displayName;
            instance.Manifest.ModpackVersion = packVersion;
            await _instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);
            progress?.Report(new DownloadProgress("modpack", "ZIP 整合包安装完成", 1, 1));
            return instance;
        }
        catch
        {
            await _instances.QuarantineAsync(instance, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task InstallLoaderAndBaseAsync(
        InstanceInfo instance,
        LoaderKind loader,
        string? loaderVersion,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (loader is not LoaderKind.Vanilla)
        {
            progress?.Report(new DownloadProgress("modpack", $"安装 {loader} {loaderVersion}", 0, 1));
            await _loaders.InstallAsync(instance, loader, loaderVersion, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        progress?.Report(new DownloadProgress("modpack", "安装原版基础文件与依赖库", 0, 1));
        await _installer.InstallAsync(instance, progress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExtractPrefixAsync(
        ZipArchive archive,
        InstanceInfo instance,
        string prefix,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var entries = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .Where(e => prefix.Length == 0
                        || e.FullName.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(e => !IsModpackMetadata(e.FullName))
            .ToList();
        long total = entries.Sum(e => Math.Max(0, e.Length));
        long completed = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullName = entry.FullName.Replace('\\', '/');
            var relative = prefix.Length == 0 ? fullName : fullName[prefix.Length..];
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var destination = SafeCombine(instance.GameDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var input = entry.Open())
            await using (var output = new FileStream(
                             destination, FileMode.Create, FileAccess.Write, FileShare.None,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            completed += Math.Max(0, entry.Length);
            progress?.Report(new DownloadProgress(
                "overrides", $"展开 {relative}", completed, Math.Max(total, completed)));
        }
    }

    private static bool IsModpackMetadata(string name)
    {
        var file = Path.GetFileName(name).ToLowerInvariant();
        return file is "instance.cfg" or "mmc-pack.json" or "modrinth.index.json"
            or "manifest.json" or "modpack.json" or "pack.json" or "modlist.html"
            or "minecraftinstance.json" or "version.json" or "setup.ini";
    }

    private static (LoaderKind Loader, string? Version) ParseCurseForgeLoader(string id)
    {
        var parts = id.Split('-', 2, StringSplitOptions.TrimEntries);
        var name = parts[0].ToLowerInvariant();
        var version = parts.Length > 1 ? parts[1] : null;
        return name switch
        {
            "forge" => (LoaderKind.Forge, version),
            "neoforge" => (LoaderKind.NeoForge, version),
            "fabric" => (LoaderKind.Fabric, version),
            "quilt" => (LoaderKind.Quilt, version),
            _ => (LoaderKind.Vanilla, null)
        };
    }

    private static string SafeCombine(string baseDirectory, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)
            || Path.IsPathRooted(relative)
            || relative.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"整合包包含不安全的路径：{relative}");
        }

        var normalizedBase = Path.GetFullPath(baseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(
            Path.Combine(normalizedBase, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"整合包路径越界：{relative}");
        }

        return full;
    }

    private static void EnsureDiskSpace(InstanceInfo instance, ModpackIndex index)
    {
        try
        {
            var root = Path.GetPathRoot(instance.Directory);
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            var drive = new DriveInfo(root);
            var required = index.Files.Sum(f => Math.Max(0, f.FileSize)) + 512L * 1024 * 1024;
            if (drive.AvailableFreeSpace < required)
            {
                throw new InvalidOperationException(
                    $"磁盘空间不足：至少需要 {required / 1024 / 1024} MB 可用空间。");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"无法检查磁盘空间：{ex.Message}");
        }
    }
}

internal sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public InlineProgress(Action<T> handler)
    {
        _handler = handler;
    }

    public void Report(T value) => _handler(value);
}
