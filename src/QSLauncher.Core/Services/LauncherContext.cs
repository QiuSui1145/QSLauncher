namespace QSLauncher.Core.Services;

/// <summary>
/// Owns the long-lived services used by the launcher. One instance per app.
/// </summary>
public sealed class LauncherContext : IDisposable
{
    public LauncherContext(PortablePaths paths, HttpService? http = null)
    {
        Paths = paths;
        Http = http ?? new HttpService();
        Mojang = new MojangService(paths, Http);
        Instances = new InstanceService(paths);
        Java = new JavaService(paths, Http);
        Installer = new VersionInstaller(paths, Http, Mojang, Instances);
        Loaders = new LoaderService(paths, Http, Mojang, Instances, Java);
        Authlib = new AuthlibInjectorService(paths, Http);
        Launcher = new GameLauncher(paths, Mojang, Installer, Instances, Authlib);
        Modrinth = new ModrinthService(Http);
        Mods = new ModService(Http, Modrinth);
        Modpacks = new ModpackInstaller(paths, Http, Instances, Loaders, Installer);
        Auth = new AuthService(Http);
        Resources = new ResourceService(paths);
        Contents = new InstanceContentService(paths);
        GameRoots = new GameRootService();
    }

    public PortablePaths Paths { get; }

    public HttpService Http { get; }

    public MojangService Mojang { get; }

    public InstanceService Instances { get; }

    public JavaService Java { get; }

    public VersionInstaller Installer { get; }

    public LoaderService Loaders { get; }

    public AuthlibInjectorService Authlib { get; }

    public GameLauncher Launcher { get; }

    public ModrinthService Modrinth { get; }

    public ModService Mods { get; }

    public ModpackInstaller Modpacks { get; }

    public AuthService Auth { get; }

    public ResourceService Resources { get; }

    public InstanceContentService Contents { get; }

    public GameRootService GameRoots { get; }

    public void Dispose()
    {
        Http.Dispose();
    }
}
