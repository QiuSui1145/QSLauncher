using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed class LoaderService
{
    private const string FabricLoaderListUrl = "https://meta.fabricmc.net/v2/versions/loader/{0}";
    private const string FabricProfileUrl = "https://meta.fabricmc.net/v2/versions/loader/{0}/{1}/profile/json";
    private const string QuiltLoaderListUrl = "https://meta.quiltmc.org/v3/versions/loader/{0}";
    private const string QuiltProfileUrl = "https://meta.quiltmc.org/v3/versions/loader/{0}/{1}/profile/json";
    private const string ForgeMetadataUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string NeoForgeMetadataUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

    private readonly PortablePaths _paths;
    private readonly HttpService _http;
    private readonly MojangService _mojang;
    private readonly InstanceService _instances;
    private readonly JavaService _java;

    public LoaderService(
        PortablePaths paths,
        HttpService http,
        MojangService mojang,
        InstanceService instances,
        JavaService java)
    {
        _paths = paths;
        _http = http;
        _mojang = mojang;
        _instances = instances;
        _java = java;
    }

    public async Task<IReadOnlyList<string>> GetLoaderVersionsAsync(
        LoaderKind loader,
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        if (loader is LoaderKind.Forge or LoaderKind.NeoForge)
        {
            return await GetInstallerLoaderVersionsAsync(loader, minecraftVersion, cancellationToken)
                .ConfigureAwait(false);
        }

        var template = loader switch
        {
            LoaderKind.Fabric => FabricLoaderListUrl,
            LoaderKind.Quilt => QuiltLoaderListUrl,
            _ => throw new NotSupportedException($"{loader} 的加载器元数据尚未接入。")
        };

        var url = new Uri(string.Format(template, Uri.EscapeDataString(minecraftVersion)));
        var text = await GetMetadataAsync(url, loader, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var versions = new List<string>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("loader", out var loaderElement)
                && loaderElement.TryGetProperty("version", out var versionElement))
            {
                var version = versionElement.GetString();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    versions.Add(version);
                }
            }
        }

        return versions;
    }

    public async Task<LoaderInstallResult> InstallAsync(
        InstanceInfo instance,
        LoaderKind loader,
        string? requestedLoaderVersion,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var minecraft = instance.Manifest.MinecraftVersion;
        if (string.IsNullOrWhiteSpace(minecraft))
        {
            throw new InvalidOperationException("实例缺少 Minecraft 版本号。");
        }

        string loaderVersion;
        if (!string.IsNullOrWhiteSpace(requestedLoaderVersion))
        {
            // Modpacks pin their loader version. Skip the metadata round-trip:
            // it is one less network dependency during installation.
            loaderVersion = requestedLoaderVersion;
        }
        else
        {
            progress?.Report(new DownloadProgress("loader", "正在读取加载器版本", 0, 1));
            var versions = await GetLoaderVersionsAsync(loader, minecraft, cancellationToken)
                .ConfigureAwait(false);
            if (versions.Count == 0)
            {
                throw new InvalidOperationException($"{loader} 没有适用于 {minecraft} 的版本。");
            }

            loaderVersion = versions[0];
        }

        if (loader is LoaderKind.Forge or LoaderKind.NeoForge)
        {
            return await InstallWithInstallerAsync(
                    instance,
                    loader,
                    loaderVersion,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var template = loader == LoaderKind.Fabric ? FabricProfileUrl : QuiltProfileUrl;
        var profileUri = new Uri(string.Format(
            template,
            Uri.EscapeDataString(minecraft),
            Uri.EscapeDataString(loaderVersion)));

        progress?.Report(new DownloadProgress("loader", $"正在安装 {loader} {loaderVersion}", 0, 1));
        var profileText = await GetMetadataAsync(profileUri, loader, cancellationToken)
            .ConfigureAwait(false);
        var profile = JsonSerializer.Deserialize<VersionInfo>(
                          profileText,
                          new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? throw new InvalidOperationException("加载器返回了空的版本描述。");
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = $"{(loader == LoaderKind.Fabric ? "fabric" : "quilt")}-loader-{loaderVersion}-{minecraft}";
        }

        profile.InheritsFrom = string.IsNullOrWhiteSpace(profile.InheritsFrom)
            ? minecraft
            : profile.InheritsFrom;

        await _mojang.SaveVersionInfoAsync(profile, cancellationToken).ConfigureAwait(false);

        instance.Manifest.VersionId = profile.Id;
        instance.Manifest.Loader = loader;
        instance.Manifest.LoaderVersion = loaderVersion;
        await _instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);

        progress?.Report(new DownloadProgress("loader", $"{loader} {loaderVersion} 已就绪", 1, 1));
        AppLog.Info($"实例 {instance.DisplayName} 使用 {loader} {loaderVersion}（{minecraft}）。");
        return new LoaderInstallResult(profile.Id, loaderVersion);
    }

    private async Task<IReadOnlyList<string>> GetInstallerLoaderVersionsAsync(
        LoaderKind loader,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(loader == LoaderKind.Forge ? ForgeMetadataUrl : NeoForgeMetadataUrl);
        var xml = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        var document = XDocument.Parse(xml);
        var all = document.Descendants("version")
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .ToList();

        if (loader == LoaderKind.Forge)
        {
            var prefix = minecraftVersion + "-";
            return all
                .Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(value => value[prefix.Length..])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Reverse()
                .ToList();
        }

        var neoPrefix = NeoForgePrefix(minecraftVersion) + ".";
        return all
            .Where(value => value.StartsWith(neoPrefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Reverse()
            .ToList();
    }

    private static string NeoForgePrefix(string minecraftVersion)
        => minecraftVersion.StartsWith("1.", StringComparison.Ordinal)
            ? minecraftVersion[2..]
            : minecraftVersion;

    private async Task<LoaderInstallResult> InstallWithInstallerAsync(
        InstanceInfo instance,
        LoaderKind loader,
        string loaderVersion,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var minecraft = instance.Manifest.MinecraftVersion;
        var normalized = loaderVersion.Trim();
        Uri installerUri;
        if (loader == LoaderKind.Forge)
        {
            var fullVersion = normalized.StartsWith(minecraft + "-", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : $"{minecraft}-{normalized}";
            if (fullVersion.StartsWith(minecraft + "-", StringComparison.OrdinalIgnoreCase))
            {
                normalized = fullVersion[(minecraft.Length + 1)..];
            }

            installerUri = new Uri(
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{fullVersion}/forge-{fullVersion}-installer.jar");
        }
        else
        {
            installerUri = new Uri(
                $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{normalized}/neoforge-{normalized}-installer.jar");
        }

        var installerDirectory = Path.Combine(_paths.CachePath, "installers");
        Directory.CreateDirectory(installerDirectory);
        var installerPath = Path.Combine(installerDirectory, Path.GetFileName(installerUri.LocalPath));
        progress?.Report(new DownloadProgress("loader", $"正在下载 {loader} 安装器", 0, 1));
        await _http.DownloadFileAsync(
                installerUri,
                installerPath,
                progress: progress,
                label: $"{loader} 安装器",
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var java = await _java.FindBestAsync(8, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (java is null)
        {
            progress?.Report(new DownloadProgress("loader", "正在准备 Java 运行时", 0, 1));
            java = await _java.InstallOfficialRuntimeAsync("jre-legacy", 8, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        var temporary = Path.Combine(_paths.CachePath, "work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            await File.WriteAllTextAsync(
                    Path.Combine(temporary, "launcher_profiles.json"),
                    "{\"profiles\":{},\"selectedProfile\":\"\",\"clientToken\":\"\"}",
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new DownloadProgress("loader", $"正在运行 {loader} 安装器", 0, 1));

            async Task<(int ExitCode, string Transcript)> RunInstallerAsync()
            {
                var attemptTranscript = new StringBuilder();
                var startInfo = new ProcessStartInfo
                {
                    FileName = java.ExecutablePath,
                    WorkingDirectory = temporary,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.ArgumentList.Add("-jar");
                startInfo.ArgumentList.Add(installerPath);
                startInfo.ArgumentList.Add("--installClient");
                startInfo.ArgumentList.Add(temporary);

                using var process = new Process { StartInfo = startInfo };
                void Append(string? line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        return;
                    }

                    AppLog.Info($"[{loader}] {line}");
                    lock (attemptTranscript)
                    {
                        if (attemptTranscript.Length < 24000)
                        {
                            attemptTranscript.AppendLine(line);
                        }
                    }
                }

                process.OutputDataReceived += (_, args) => Append(args.Data);
                process.ErrorDataReceived += (_, args) => Append(args.Data);
                if (!process.Start())
                {
                    throw new InvalidOperationException($"{loader} 安装器无法启动。");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeout.Token);
                try
                {
                    await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // The installer may have already exited.
                    }

                    throw new TimeoutException($"{loader} 安装器运行超过 15 分钟，已停止。");
                }

                process.WaitForExit();
                return (process.ExitCode, attemptTranscript.ToString());
            }

            var exitCode = -1;
            var lastTranscript = string.Empty;
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await RunInstallerAsync().ConfigureAwait(false);
                exitCode = result.ExitCode;
                lastTranscript = result.Transcript;
                if (exitCode == 0)
                {
                    break;
                }

                if (attempt == 1)
                {
                    AppLog.Warn($"{loader} 安装器第一次失败（退出码 {exitCode}），准备重试。");
                    progress?.Report(new DownloadProgress(
                        "loader", "安装器下载中断，正在重试", 0, 1));
                    await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
                }
            }

            if (exitCode != 0)
            {
                var lines = lastTranscript.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .TakeLast(8);
                throw new InvalidOperationException(
                    $"{loader} 安装器失败（退出码 {exitCode}）。\n" +
                    string.Join('\n', lines));
            }

            var versionsDirectory = Path.Combine(temporary, "versions");
            if (!Directory.Exists(versionsDirectory))
            {
                throw new InvalidOperationException($"{loader} 安装器没有生成版本目录。");
            }

            var installedVersionId = string.Empty;
            foreach (var versionDirectory in Directory.EnumerateDirectories(versionsDirectory))
            {
                var target = Path.Combine(_paths.VersionsPath, Path.GetFileName(versionDirectory));
                CopyDirectory(versionDirectory, target);
                if (Path.GetFileName(versionDirectory).Contains("forge", StringComparison.OrdinalIgnoreCase))
                {
                    installedVersionId = Path.GetFileName(versionDirectory);
                }
            }

            var librariesDirectory = Path.Combine(temporary, "libraries");
            if (Directory.Exists(librariesDirectory))
            {
                CopyDirectory(librariesDirectory, _paths.LibrariesPath);
            }

            if (string.IsNullOrWhiteSpace(installedVersionId))
            {
                installedVersionId = Path.GetFileName(Directory.EnumerateDirectories(versionsDirectory)
                    .OrderByDescending(path => Path.GetFileName(path).Length)
                    .First());
            }

            instance.Manifest.VersionId = installedVersionId;
            instance.Manifest.Loader = loader;
            instance.Manifest.LoaderVersion = normalized;
            await _instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);
            progress?.Report(new DownloadProgress("loader", $"{loader} {normalized} 已就绪", 1, 1));
            AppLog.Info($"实例 {instance.DisplayName} 使用 {loader} {normalized}（{minecraft}）。");
            return new LoaderInstallResult(installedVersionId, normalized);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporary))
                {
                    Directory.Delete(temporary, true);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"清理 {loader} 安装临时目录失败：{ex.Message}");
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            var targetInfo = new FileInfo(target);
            var sourceInfo = new FileInfo(file);
            if (targetInfo.Exists && targetInfo.Length == sourceInfo.Length)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private async Task<string> GetMetadataAsync(
        Uri official,
        LoaderKind loader,
        CancellationToken cancellationToken)
    {
        // All metadata and downloads stay on the loader's official endpoint.
        // No mirror or launcher-side relay is involved.
        return await _http.GetStringAsync(official, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record LoaderInstallResult(string VersionId, string LoaderVersion);
