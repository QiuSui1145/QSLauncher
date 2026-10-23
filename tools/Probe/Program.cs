using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "probe-data"));
var paths = new PortablePaths(root);
paths.EnsureLayout();
AppLog.Initialize(paths.LogsPath);
using var context = new LauncherContext(paths);

Console.WriteLine("manifest...");
var manifest = await context.Mojang.GetManifestAsync(refresh: true);
Console.WriteLine($"latest={manifest.Latest.Release} snapshots={manifest.Versions.Count}");

var release = manifest.Versions.First(v => v.Id == manifest.Latest.Release);
var info = await context.Mojang.ResolveAsync(release.Id);
Console.WriteLine($"id={info.Id} base={info.BaseId} main={info.MainClass} libs={info.Libraries.Count} assets={info.Assets} java={info.JavaVersion?.MajorVersion}");

Console.WriteLine("java...");
var runtimes = await context.Java.ScanAsync(refresh: true);
foreach (var runtime in runtimes)
{
    Console.WriteLine($"  {runtime.DisplayName} {runtime.VersionString} {runtime.Architecture} portable={runtime.IsPortable} {runtime.ExecutablePath}");
}

if (args.Contains("modpack"))
{
    var project = args.SkipWhile(a => a != "modpack").Skip(1).FirstOrDefault() ?? "1KVo5zza";
    var versions = await context.Modrinth.GetVersionsAsync(project);
    var version = versions.FirstOrDefault(v => v.Files.Any(f => f.Filename.EndsWith(".mrpack")))
                  ?? throw new InvalidOperationException("no mrpack version");
    Console.WriteLine($"pack={project} version={version.Name} {version.VersionNumber} game={string.Join(",", version.GameVersions)} loaders={string.Join(",", version.Loaders)}");
    var last = -1;
    var progress = new Progress<DownloadProgress>(p =>
    {
        if (p.Percent != last)
        {
            last = p.Percent;
            Console.WriteLine($"  [{p.Percent,3}%] {p.Stage} {p.Item}");
        }
    });
    var instance = await context.Modpacks.InstallFromVersionAsync(version, project, progress);
    Console.WriteLine($"INSTALLED={instance.Directory} LOADER={instance.Manifest.Loader} {instance.Manifest.LoaderVersion} VERSION={instance.Manifest.VersionId}");
}

if (args.Contains("modpackvid"))
{
    var versionId = args.SkipWhile(a => a != "modpackvid").Skip(1).FirstOrDefault()
                    ?? throw new InvalidOperationException("missing version id");
    var project = args.SkipWhile(a => a != "modpackvid").Skip(2).FirstOrDefault() ?? "uBPAMdwq";
    var version = await context.Modrinth.GetVersionAsync(versionId);
    Console.WriteLine(
        $"pack={project} version={version.Name} {version.VersionNumber} game={string.Join(",", version.GameVersions)} loaders={string.Join(",", version.Loaders)}");
    var last = -1;
    var progress = new Progress<DownloadProgress>(p =>
    {
        if (p.Percent != last)
        {
            last = p.Percent;
            Console.WriteLine($"  [{p.Percent,3}%] {p.Stage} {p.Item}");
        }
    });
    var instance = await context.Modpacks.InstallFromVersionAsync(version, project, progress);
    Console.WriteLine(
        $"INSTALLED={instance.Directory} LOADER={instance.Manifest.Loader} {instance.Manifest.LoaderVersion} VERSION={instance.Manifest.VersionId}");
}

if (args.Any(a => a.StartsWith("plan", StringComparison.OrdinalIgnoreCase)))
{
    var instances = await context.Instances.ListAsync();
    var instance = args.Contains("plan-forge")
        ? instances.First(i => i.Manifest.Loader == LoaderKind.Forge)
        : instances.First();
    var requiredJava = info.JavaVersion?.MajorVersion ?? 25;
    var best = await context.Java.FindBestAsync(requiredJava);
    if (best is null)
    {
        throw new InvalidOperationException("no java");
    }

    var account = args.Contains("plan-yggdrasil")
        ? new GameAccount
        {
            Name = "Probe",
            Uuid = GameAccount.OfflineUuid("Probe"),
            AccessToken = "test-token",
            UserType = "mojang",
            Kind = AccountKind.Yggdrasil,
            ServerUrl = "https://littleskin.cn/api/yggdrasil"
        }
        : GameAccount.Offline("Probe");
    var plan = await context.Launcher.BuildPlanAsync(
        instance,
        account,
        best,
        new LaunchOptions { MemoryMb = 2048 });
    Console.WriteLine($"JAVA={plan.JavaExecutable}");
    Console.WriteLine($"ARGS={plan.Arguments.Count}");
    Console.WriteLine($"LOGPATH={plan.LogPath}");
    foreach (var argument in plan.Arguments.Take(18))
    {
        Console.WriteLine($"  {argument}");
    }

    Console.WriteLine($"  CLASSES={plan.Arguments.Count(a => a.Contains("libraries"))}");
}

if (args.Contains("forge"))
{
    var forgeVersions = await context.Loaders.GetLoaderVersionsAsync(
        LoaderKind.Forge,
        release.Id);
    Console.WriteLine($"FORGE_VERSIONS={forgeVersions.Count} latest={string.Join(",", forgeVersions.Take(3))}");
    var neoVersions = await context.Loaders.GetLoaderVersionsAsync(
        LoaderKind.NeoForge,
        release.Id);
    Console.WriteLine($"NEO_VERSIONS={neoVersions.Count} latest={string.Join(",", neoVersions.Take(3))}");
    var instance = await context.Instances.CreateAsync("Probe Forge 26.3", release.Id, release.Id);
    Console.WriteLine($"instance={instance.Directory}");
    var loaderProgress = new Progress<DownloadProgress>(p => Console.WriteLine($"  [{p.Percent,3}%] {p.Stage} {p.Item}"));
    var installed = await context.Loaders.InstallAsync(
        instance,
        LoaderKind.Forge,
        forgeVersions.FirstOrDefault(),
        loaderProgress);
    Console.WriteLine($"FORGE_INSTALLED={installed.VersionId} loader={installed.LoaderVersion}");
}

if (args.Contains("assets"))
{
    var instance = (await context.Instances.ListAsync()).First();
    Console.WriteLine($"assets for {instance.DisplayName}");
    var last = -1;
    var progress = new Progress<DownloadProgress>(p =>
    {
        if (p.Percent != last)
        {
            last = p.Percent;
            Console.WriteLine($"  [{p.Percent,3}%] {p.Stage} {p.Item}");
        }
    });
    await context.Installer.EnsureRuntimeFilesAsync(
        instance, info, includeAssets: true, progress, CancellationToken.None);
    Console.WriteLine("assets ready");
}

if (args.Contains("install"))
{
    Console.WriteLine("creating instance...");
    var instance = await context.Instances.CreateAsync("Probe 26.3", release.Id, release.Id);
    Console.WriteLine($"instance={instance.Directory}");
    var lastPercent = -1;
    var progress = new Progress<DownloadProgress>(p =>
    {
        if (p.Percent != lastPercent)
        {
            lastPercent = p.Percent;
            Console.WriteLine($"  [{p.Percent,3}%] {p.Stage} {p.Item}");
        }
    });
    await context.Installer.EnsureRuntimeFilesAsync(
        instance, info, includeAssets: false, progress, CancellationToken.None);
    Console.WriteLine("runtime files ready");
}

if (args.Contains("moddeps"))
{
    var query = args.SkipWhile(a => a != "moddeps").Skip(1).FirstOrDefault() ?? "iris";
    var instance = (await context.Instances.ListAsync())
        .First(i => i.Manifest.Loader == LoaderKind.Fabric);
    var hits = await context.Modrinth.SearchAsync(
        query, "mod", 5, 0, instance.Manifest.MinecraftVersion, "fabric");
    var hit = hits.Hits.FirstOrDefault()
              ?? throw new InvalidOperationException("no mod hit");
    Console.WriteLine($"MOD={hit.Title} {hit.ProjectId}");
    var versions = await context.Modrinth.GetVersionsAsync(
        hit.ProjectId, instance.Manifest.MinecraftVersion, "fabric");
    var version = versions.First();
    Console.WriteLine(
        $"VERSION={version.VersionNumber} DEPS={string.Join(",", version.Dependencies.Select(d => $"{d.ProjectId}:{d.DependencyType}"))}");
    var last = -1;
    var progress = new Progress<DownloadProgress>(p =>
    {
        if (p.Percent != last)
        {
            last = p.Percent;
            Console.WriteLine($"  [{p.Percent,3}%] {p.Stage} {p.Item}");
        }
    });
    var installed = await context.Mods.InstallAsync(instance, hit, progress);
    Console.WriteLine($"INSTALLED_MOD={installed.DisplayName} {installed.FileName}");
    foreach (var mod in await context.Mods.ScanAsync(instance))
    {
        Console.WriteLine($"  MODFILE={mod.FileName} project={mod.ProjectId ?? "-"}");
    }
}

Console.WriteLine("modrinth...");
var search = await context.Modrinth.SearchAsync("", "modpack", 3);
Console.WriteLine($"total={search.TotalHits} hits={search.Hits.Count}");
foreach (var hit in search.Hits)
{
    Console.WriteLine($"  {hit.Title} {hit.Downloads} {hit.ProjectId}");
}
