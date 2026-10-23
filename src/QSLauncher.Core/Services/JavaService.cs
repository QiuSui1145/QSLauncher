using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed partial class JavaService
{
    public const string RuntimeIndexUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    private readonly PortablePaths _paths;
    private readonly HttpService _http;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private List<JavaRuntimeInfo>? _cache;

    public JavaService(PortablePaths paths, HttpService http)
    {
        _paths = paths;
        _http = http;
    }

    public async Task<IReadOnlyList<JavaRuntimeInfo>> ScanAsync(
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!refresh && _cache is not null)
        {
            return _cache;
        }

        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh && _cache is not null)
            {
                return _cache;
            }

            var result = new List<JavaRuntimeInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var executable in FindPortableExecutables())
            {
                AddIfUnique(result, seen, await ProbeAsync(executable, true, cancellationToken)
                    .ConfigureAwait(false));
            }

            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrWhiteSpace(javaHome))
            {
                var executable = Path.Combine(javaHome, "bin", "java.exe");
                AddIfUnique(result, seen, await ProbeAsync(executable, false, cancellationToken)
                    .ConfigureAwait(false));
            }

            foreach (var executable in FindPathJava())
            {
                AddIfUnique(result, seen, await ProbeAsync(executable, false, cancellationToken)
                    .ConfigureAwait(false));
            }

            foreach (var executable in FindCommonInstallations())
            {
                AddIfUnique(result, seen, await ProbeAsync(executable, false, cancellationToken)
                    .ConfigureAwait(false));
            }

            _cache = result
                .OrderByDescending(j => j.IsPortable)
                .ThenByDescending(j => j.MajorVersion)
                .ThenBy(j => j.Vendor, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return _cache;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    public async Task<JavaRuntimeInfo?> FindBestAsync(
        int requiredMajor,
        string? preferredComponent = null,
        CancellationToken cancellationToken = default)
    {
        var runtimes = await ScanAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (runtimes.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredComponent))
        {
            var preferred = runtimes.FirstOrDefault(j =>
                string.Equals(j.Id, preferredComponent, StringComparison.OrdinalIgnoreCase)
                || string.Equals(j.ExecutablePath, preferredComponent, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null && preferred.MajorVersion >= requiredMajor)
            {
                return preferred;
            }
        }

        var exact = runtimes
            .Where(j => j.MajorVersion == requiredMajor)
            .OrderByDescending(j => j.IsPortable)
            .FirstOrDefault();
        if (exact is not null)
        {
            return exact;
        }

        var compatible = runtimes
            .Where(j => j.MajorVersion >= requiredMajor)
            .OrderBy(j => j.MajorVersion - requiredMajor)
            .ThenByDescending(j => j.IsPortable)
            .FirstOrDefault();
        return compatible ?? runtimes.OrderByDescending(j => j.MajorVersion).First();
    }

    public async Task<JavaRuntimeInfo> InstallOfficialRuntimeAsync(
        string? preferredComponent,
        int requiredMajor,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var indexBytes = await _http.GetBytesAsync(new Uri(RuntimeIndexUrl), cancellationToken)
            .ConfigureAwait(false);
        using var index = JsonDocument.Parse(indexBytes);
        var platformKey = MojangService.CurrentOsName == "windows"
            ? (MojangService.CurrentOsArch == "x86" ? "windows-x86" : "windows-x64")
            : $"{MojangService.CurrentOsName}-{MojangService.CurrentOsArch}";

        if (!index.RootElement.TryGetProperty(platformKey, out var platform))
        {
            throw new InvalidOperationException($"Mojang 没有为 {platformKey} 提供 Java 运行时。");
        }

        var candidates = new List<RuntimeCandidate>();
        foreach (var component in platform.EnumerateObject())
        {
            if (component.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in component.Value.EnumerateArray())
            {
                if (!entry.TryGetProperty("manifest", out var manifest)
                    || !manifest.TryGetProperty("url", out var manifestUrl)
                    || manifestUrl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var versionName = entry.TryGetProperty("version", out var version)
                                  && version.TryGetProperty("name", out var name)
                    ? name.GetString() ?? string.Empty
                    : string.Empty;

                var major = ExtractMajor(versionName);
                candidates.Add(new RuntimeCandidate(
                    component.Name,
                    versionName,
                    major,
                    manifestUrl.GetString() ?? string.Empty));
            }
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("Mojang Java 运行时清单为空。");
        }

        var chosen = candidates.FirstOrDefault(c =>
                         !string.IsNullOrWhiteSpace(preferredComponent)
                         && c.Component.Equals(preferredComponent, StringComparison.OrdinalIgnoreCase)
                         && c.Major >= requiredMajor)
                     ?? candidates
                         .Where(c => c.Major == requiredMajor)
                         .OrderByDescending(c => c.VersionName, StringComparer.OrdinalIgnoreCase)
                         .FirstOrDefault()
                     ?? candidates
                         .Where(c => c.Major >= requiredMajor)
                         .OrderBy(c => c.Major)
                         .ThenByDescending(c => c.VersionName, StringComparer.OrdinalIgnoreCase)
                         .FirstOrDefault()
                     ?? candidates.OrderByDescending(c => c.Major).First();

        var installDirectory = Path.Combine(_paths.JavaPath, chosen.Component);
        if (File.Exists(Path.Combine(installDirectory, "bin", "java.exe")))
        {
            var existing = await ProbeAsync(
                Path.Combine(installDirectory, "bin", "java.exe"), true, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }
        }

        var manifestCache = Path.Combine(_paths.JavaPath, "manifests", chosen.Component + ".json");
        var manifestJson = await _http.GetStringAsync(new Uri(chosen.ManifestUrl), cancellationToken)
            .ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestCache)!);
        await File.WriteAllTextAsync(manifestCache, manifestJson, cancellationToken).ConfigureAwait(false);

        using var manifestDocument = JsonDocument.Parse(manifestJson);
        if (!manifestDocument.RootElement.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Java 运行时清单缺少文件列表。");
        }

        var downloads = new List<RuntimeFile>();
        long totalBytes = 0;
        foreach (var file in files.EnumerateObject())
        {
            var relative = file.Name.Replace('/', Path.DirectorySeparatorChar);
            var type = file.Value.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : "file";
            if (type == "directory")
            {
                continue;
            }

            if (type == "link")
            {
                continue;
            }

            if (!file.Value.TryGetProperty("downloads", out var downloadElement)
                || !downloadElement.TryGetProperty("raw", out var raw)
                || !raw.TryGetProperty("url", out var urlElement))
            {
                continue;
            }

            var url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var size = raw.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;
            var sha1 = raw.TryGetProperty("sha1", out var sha1Element) ? sha1Element.GetString() : null;
            totalBytes += size;
            downloads.Add(new RuntimeFile(
                Path.Combine(installDirectory, relative),
                url,
                sha1,
                size,
                type == "executable"));
        }

        Directory.CreateDirectory(installDirectory);
        long completed = 0;
        var totalItems = downloads.Count;
        for (var indexItem = 0; indexItem < downloads.Count; indexItem++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = downloads[indexItem];
            var baseBytes = completed;
            await _http.DownloadFileAsync(
                new Uri(file.Url),
                file.Destination,
                file.Sha1,
                null,
                file.Size,
                new Progress<DownloadProgress>(p =>
                {
                    progress?.Report(new DownloadProgress(
                        "java",
                        $"Java 运行时 · {Path.GetFileName(file.Destination)}",
                        baseBytes + p.BytesReceived,
                        totalBytes,
                        indexItem,
                        totalItems));
                }),
                Path.GetFileName(file.Destination),
                cancellationToken).ConfigureAwait(false);

            completed += file.Size;
            if (file.Executable && File.Exists(file.Destination) && !OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(
                        file.Destination,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or IOException)
                {
                    // Some Unix-like file systems do not expose mode bits.
                }
            }

            progress?.Report(new DownloadProgress(
                "java",
                $"Java 运行时 · {Path.GetFileName(file.Destination)}",
                completed,
                totalBytes,
                indexItem + 1,
                totalItems));
        }

        var executablePath = Path.Combine(installDirectory, "bin", "java.exe");
        if (!File.Exists(executablePath))
        {
            executablePath = Path.Combine(installDirectory, "bin", "java");
        }

        var runtime = await ProbeAsync(executablePath, true, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidDataException("Java 运行时解压完成，但无法执行 java。");
        _cache = null;
        AppLog.Info($"已安装 Java 运行时 {runtime.DisplayName}：{runtime.ExecutablePath}");
        return runtime;
    }

    private IEnumerable<string> FindPortableExecutables()
    {
        if (!Directory.Exists(_paths.JavaPath))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(_paths.JavaPath))
        {
            foreach (var executable in CandidateExecutables(directory).Take(1))
            {
                yield return executable;
            }
        }
    }

    private static IEnumerable<string> CandidateExecutables(string root)
    {
        var direct = Path.Combine(root, "bin", "java.exe");
        if (File.Exists(direct))
        {
            yield return direct;
        }

        var unix = Path.Combine(root, "bin", "java");
        if (File.Exists(unix))
        {
            yield return unix;
        }
    }

    private static IEnumerable<string> FindPathJava()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var executable = Path.Combine(trimmed, "java.exe");
            if (File.Exists(executable))
            {
                yield return executable;
            }
        }
    }

    private static IEnumerable<string> FindCommonInstallations()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zulu"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Amazon Corretto"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium")
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var executable = Path.Combine(directory, "bin", "java.exe");
                if (File.Exists(executable))
                {
                    yield return executable;
                }
            }
        }
    }

    public static async Task<JavaRuntimeInfo?> ProbeAsync(
        string executable,
        bool portable,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executable))
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo(executable, "-version")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore a process that already exited.
                }

                return null;
            }

            var output = (await errorTask.ConfigureAwait(false))
                         + Environment.NewLine
                         + (await outputTask.ConfigureAwait(false));
            var versionMatch = JavaVersionRegex().Match(output);
            if (!versionMatch.Success)
            {
                return null;
            }

            var versionString = versionMatch.Groups[1].Value;
            var major = ExtractMajor(versionString);
            var vendor = DetectVendor(executable);
            var id = portable
                ? $"portable-{Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(executable)))}"
                : $"system-{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                    System.Text.Encoding.UTF8.GetBytes(executable)))[..8].ToLowerInvariant()}";

            return new JavaRuntimeInfo
            {
                Id = id,
                ExecutablePath = executable,
                VersionString = versionString,
                MajorVersion = major,
                Vendor = vendor,
                Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
                IsPortable = portable,
                Source = portable ? "portable" : "system"
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Warn($"Java 探测失败：{executable} · {ex.Message}");
            return null;
        }
    }

    private static void AddIfUnique(
        ICollection<JavaRuntimeInfo> result,
        ISet<string> seen,
        JavaRuntimeInfo? runtime)
    {
        if (runtime is not null && seen.Add(runtime.ExecutablePath))
        {
            result.Add(runtime);
        }
    }

    private static string DetectVendor(string executable)
    {
        var path = executable.ToLowerInvariant();
        if (path.Contains("eclipse adoptium") || path.Contains("adoptium")) return "Eclipse Adoptium";
        if (path.Contains("microsoft")) return "Microsoft";
        if (path.Contains("zulu")) return "Azul Zulu";
        if (path.Contains("corretto")) return "Amazon Corretto";
        if (path.Contains("graal")) return "GraalVM";
        if (path.Contains("mojang") || path.Contains("java-runtime")) return "Mojang";
        return "系统 Java";
    }

    private static int ExtractMajor(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return 0;
        }

        var span = version.AsSpan().Trim();
        var dot = span.IndexOf('.');
        var first = dot >= 0 ? span[..dot] : span;
        if (!int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var major))
        {
            return 0;
        }

        if (major == 1 && dot >= 0)
        {
            var rest = span[(dot + 1)..];
            var secondDot = rest.IndexOf('.');
            var second = secondDot >= 0 ? rest[..secondDot] : rest;
            if (int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacy))
            {
                return legacy;
            }
        }

        return major;
    }

    [GeneratedRegex("version\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex JavaVersionRegex();

    private sealed record RuntimeCandidate(string Component, string VersionName, int Major, string ManifestUrl);

    private sealed record RuntimeFile(string Destination, string Url, string? Sha1, long Size, bool Executable);
}
