namespace QSLauncher.App.ViewModels;

/// <summary>
/// The download page owns the official-version browser. Reusing the existing
/// download state keeps installation/progress logic in one place while the
/// local version page stays focused on isolated instances.
/// </summary>
public sealed class DownloadsViewModel : ObservableObject
{
    public DownloadsViewModel(VersionsViewModel core)
    {
        Core = core;
    }

    public VersionsViewModel Core { get; }
}
