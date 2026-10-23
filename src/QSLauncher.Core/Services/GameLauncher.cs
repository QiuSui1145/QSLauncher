using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed partial class GameLauncher
{
    private readonly PortablePaths _paths;
    private readonly MojangService _mojang;
    private readonly VersionInstaller _installer;
    private readonly InstanceService _instances;
    private readonly AuthlibInjectorService _authlib;

    public GameLauncher(
        PortablePaths paths,
        MojangService mojang,
        VersionInstaller installer,
        InstanceService instances,
        AuthlibInjectorService authlib)
    {
        _paths = paths;
        _mojang = mojang;
        _installer = installer;
        _instances = instances;
        _authlib = authlib;
    }

    public async Task<GameSession> LaunchAsync(
        InstanceInfo instance,
        GameAccount account,
        JavaRuntimeInfo java,
        LaunchOptions options,
        IProgress<DownloadProgress>? installProgress = null,
        Action<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(
                instance, account, java, options, installProgress, cancellationToken)
            .ConfigureAwait(false);
        var startInfo = new ProcessStartInfo(plan.JavaExecutable)
        {
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var logPath = plan.LogPath;

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var logGate = new object();
        var logWriter = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true };
        void WriteLine(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (logGate)
            {
                try
                {
                    logWriter.WriteLine(line);
                }
                catch (ObjectDisposedException)
                {
                    // Process output can arrive after the window closes.
                }
            }

            output?.Invoke(line);
        }

        process.OutputDataReceived += (_, e) => WriteLine(e.Data);
        process.ErrorDataReceived += (_, e) => WriteLine(e.Data);
        process.Exited += (_, _) =>
        {
            try
            {
                logWriter.Dispose();
            }
            catch
            {
                // Already disposed.
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 Java 进程。");
            }
        }
        catch
        {
            logWriter.Dispose();
            process.Dispose();
            throw;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        instance.Manifest.LastPlayedAt = DateTimeOffset.Now;
        await _instances.SaveAsync(instance, cancellationToken).ConfigureAwait(false);

        AppLog.Info($"启动实例 {instance.DisplayName}，Java {java.MajorVersion}，内存 {plan.MemoryMb} MB。");
        return new GameSession
        {
            Process = process,
            LogPath = logPath,
            Instance = instance
        };
    }

    public async Task<LaunchPlan> BuildPlanAsync(
        InstanceInfo instance,
        GameAccount account,
        JavaRuntimeInfo java,
        LaunchOptions options,
        IProgress<DownloadProgress>? installProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(java.ExecutablePath))
        {
            throw new FileNotFoundException("Java 可执行文件不存在。", java.ExecutablePath);
        }

        var sourceRoot = string.IsNullOrWhiteSpace(instance.Manifest.SourceRoot)
            ? null
            : Path.GetFullPath(instance.Manifest.SourceRoot);
        // Linked versions keep their files in place. If the version folder is
        // an isolated modpack, InstanceInfo.GameDirectory points at
        // .minecraft/versions/<id>; otherwise it points at the shared root.
        var workingDirectory = instance.GameDirectory;
        var versionId = sourceRoot is null
            ? instance.Manifest.VersionId
            : instance.Manifest.SourceVersionId ?? instance.Manifest.VersionId;
        var version = sourceRoot is null
            ? await _mojang.ResolveAsync(versionId, cancellationToken).ConfigureAwait(false)
            : await _mojang.ResolveFromRootAsync(sourceRoot, versionId, cancellationToken)
                .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(version.MainClass))
        {
            throw new InvalidOperationException("版本 JSON 缺少主类，无法启动。");
        }

        var required = version.JavaVersion?.MajorVersion ?? 8;
        if (java.MajorVersion > 0 && java.MajorVersion < required)
        {
            throw new InvalidOperationException(
                $"该版本需要 Java {required} 或更高，当前为 Java {java.MajorVersion}。");
        }

        string assetsRoot;
        string librariesRoot;
        string classpath;
        if (sourceRoot is null)
        {
            await _installer.EnsureRuntimeFilesAsync(
                    instance,
                    version,
                    includeAssets: true,
                    installProgress,
                    cancellationToken)
                .ConfigureAwait(false);
            assetsRoot = _paths.AssetsPath;
            librariesRoot = _paths.LibrariesPath;
            classpath = BuildClasspath(version, instance);
        }
        else
        {
            installProgress?.Report(new DownloadProgress(
                "external", "Loading external .minecraft version", 0, 1));
            librariesRoot = Path.Combine(sourceRoot, "libraries");
            assetsRoot = Path.Combine(sourceRoot, "assets");
            var assetIndex = version.AssetIndex?.Id ?? version.Assets;
            if (!File.Exists(Path.Combine(assetsRoot, "indexes", assetIndex + ".json")))
            {
                // Shared launcher assets are safe to reuse when the external
                // directory did not download them yet.
                assetsRoot = _paths.AssetsPath;
            }

            classpath = BuildExternalClasspath(version, instance, sourceRoot, librariesRoot);
            _installer.PrepareExternalNatives(
                version, sourceRoot, librariesRoot, instance.NativesDirectory);
        }
        if (string.IsNullOrWhiteSpace(classpath))
        {
            throw new InvalidOperationException("依赖库不完整，无法构建启动类路径。");
        }

        string? authlibArgument = null;
        if (account.Kind == AccountKind.Yggdrasil)
        {
            if (string.IsNullOrWhiteSpace(account.ServerUrl))
            {
                throw new InvalidOperationException("第三方账号缺少 Yggdrasil 服务器地址。");
            }

            installProgress?.Report(new DownloadProgress(
                "authlib", "正在准备 authlib-injector", 0, 1));
            var jar = await _authlib.EnsureAsync(installProgress, cancellationToken)
                .ConfigureAwait(false);
            authlibArgument = AuthlibInjectorService.BuildJavaAgentArgument(
                jar, account.ServerUrl);
        }

        var features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["is_demo_user"] = options.Demo,
            ["has_custom_resolution"] = options.Width is > 0 && options.Height is > 0,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        };

        var replacements = BuildReplacements(
            instance, version, account, java, classpath, options, assetsRoot, librariesRoot,
            workingDirectory);
        var arguments = new List<string>();
        var xmx = Math.Clamp(options.MemoryMb, 512, 32768);
        arguments.Add($"-Xmx{xmx}M");
        arguments.Add($"-Xms{Math.Min(1024, xmx / 2)}M");
        arguments.Add("-Dfile.encoding=UTF-8");
        arguments.Add("-Dstdout.encoding=UTF-8");
        arguments.Add("-Dstderr.encoding=UTF-8");
        if (!version.JvmArguments
                .SelectMany(entry => entry.Values)
                .Any(value => value.Contains("java.library.path", StringComparison.OrdinalIgnoreCase)))
        {
            arguments.Add("-Djava.library.path=" + instance.NativesDirectory);
        }

        arguments.Add("-Dlog4j2.formatMsgNoLookups=true");
        if (!string.IsNullOrWhiteSpace(authlibArgument))
        {
            arguments.Add(authlibArgument);
        }

        var loggingArgument = VersionInstaller.GetLoggingArgument(version, _paths);
        if (!string.IsNullOrWhiteSpace(loggingArgument))
        {
            arguments.Add(Substitute(loggingArgument, replacements));
        }

        foreach (var entry in version.JvmArguments)
        {
            if (!MojangService.RulesAllow(entry.Rules, features))
            {
                continue;
            }

            foreach (var value in entry.Values)
            {
                arguments.Add(Substitute(value, replacements));
            }
        }

        arguments.AddRange(SplitArguments(options.JvmArgs));
        if (!arguments.Any(a => a == "-cp" || a == "-classpath"))
        {
            arguments.Add("-cp");
            arguments.Add(classpath);
        }

        arguments.Add(version.MainClass);

        if (version.GameArguments.Count > 0)
        {
            foreach (var entry in version.GameArguments)
            {
                if (!MojangService.RulesAllow(entry.Rules, features))
                {
                    continue;
                }

                foreach (var value in entry.Values)
                {
                    arguments.Add(Substitute(value, replacements));
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(version.LegacyMinecraftArguments))
        {
            foreach (var value in SplitArguments(version.LegacyMinecraftArguments))
            {
                arguments.Add(Substitute(value, replacements));
            }
        }

        arguments.AddRange(SplitArguments(options.GameArgs));

        var logDirectory = Path.Combine(_paths.LogsPath, "games");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(
            logDirectory,
            $"{instance.Manifest.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        return new LaunchPlan(arguments, logPath, java.ExecutablePath, xmx, workingDirectory);
    }

    public string BuildClasspath(ResolvedVersion version, InstanceInfo instance)
    {
        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var clientJar = Path.Combine(_paths.VersionsPath, version.BaseId + ".jar");
        if (File.Exists(clientJar) && seen.Add(clientJar))
        {
            entries.Add(clientJar);
        }

        foreach (var library in version.Libraries)
        {
            if (!MojangService.RulesAllow(library.Rules))
            {
                continue;
            }

            var relative = !string.IsNullOrWhiteSpace(library.Downloads?.Artifact?.Path)
                ? library.Downloads.Artifact.Path
                : MojangService.GetMavenPath(library.Name);
            var path = Path.Combine(_paths.LibrariesPath, relative!.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path) && seen.Add(path))
            {
                entries.Add(path);
            }
        }

        return string.Join(Path.PathSeparator, entries);
    }

    private string BuildExternalClasspath(
        ResolvedVersion version,
        InstanceInfo instance,
        string sourceRoot,
        string librariesRoot)
    {
        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var baseJar = Path.Combine(sourceRoot, "versions", version.BaseId, version.BaseId + ".jar");
        var patchedJar = Path.Combine(sourceRoot, "versions", version.Id, version.Id + ".jar");
        foreach (var jar in new[] { baseJar, patchedJar })
        {
            if (File.Exists(jar) && seen.Add(jar))
            {
                entries.Add(jar);
            }
        }

        foreach (var library in version.Libraries)
        {
            if (!MojangService.RulesAllow(library.Rules))
            {
                continue;
            }

            var relative = !string.IsNullOrWhiteSpace(library.Downloads?.Artifact?.Path)
                ? library.Downloads.Artifact.Path
                : MojangService.GetMavenPath(library.Name);
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var path = Path.Combine(librariesRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                path = Path.Combine(_paths.LibrariesPath, relative.Replace('/', Path.DirectorySeparatorChar));
            }

            if (File.Exists(path) && seen.Add(path))
            {
                entries.Add(path);
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                $"挂载版本缺少客户端与依赖库，无法启动。请先用官方启动器补全：{sourceRoot}");
        }

        return string.Join(Path.PathSeparator, entries);
    }

    private Dictionary<string, string> BuildReplacements(
        InstanceInfo instance,
        ResolvedVersion version,
        GameAccount account,
        JavaRuntimeInfo java,
        string classpath,
        LaunchOptions options,
        string assetsRoot,
        string librariesRoot,
        string workingDirectory)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = account.Name,
            ["version_name"] = instance.Manifest.VersionId,
            ["game_directory"] = workingDirectory,
            ["assets_root"] = assetsRoot,
            ["assets_index_name"] = version.AssetIndex?.Id ?? version.Assets,
            ["auth_uuid"] = account.Uuid,
            ["auth_access_token"] = account.AccessToken,
            ["auth_session"] = account.AccessToken,
            ["user_type"] = account.UserType,
            ["version_type"] = version.Type,
            ["user_properties"] = account.UserProperties,
            ["clientid"] = account.ClientId ?? string.Empty,
            ["auth_xuid"] = account.Xuid ?? string.Empty,
            ["launcher_name"] = "QS Launcher",
            ["launcher_version"] = AppInfo.Version,
            ["natives_directory"] = instance.NativesDirectory,
            ["classpath"] = classpath,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = librariesRoot,
            ["resolution_width"] = (options.Width ?? 1280).ToString(),
            ["resolution_height"] = (options.Height ?? 720).ToString(),
            ["quickPlayPath"] = string.Empty,
            ["quickPlaySingleplayer"] = string.Empty,
            ["quickPlayMultiplayer"] = string.Empty,
            ["quickPlayRealms"] = string.Empty,
            ["java_version"] = java.MajorVersion.ToString()
        };
    }

    public static string Substitute(string value, IReadOnlyDictionary<string, string> replacements)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return PlaceholderRegex().Replace(value, match =>
        {
            var key = match.Groups[1].Value;
            return replacements.TryGetValue(key, out var replacement) ? replacement : match.Value;
        });
    }

    public static List<string> SplitArguments(string? value)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        var current = new StringBuilder();
        var quoted = false;
        var quoteChar = '"';
        foreach (var character in value)
        {
            if (quoted)
            {
                if (character == quoteChar)
                {
                    quoted = false;
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quoted = true;
                quoteChar = character;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    [GeneratedRegex("\\$\\{([A-Za-z0-9_]+)\\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();
}
