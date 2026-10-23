using System.Collections.ObjectModel;
using System.Windows.Input;
using QSLauncher.App.Services;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public sealed class LoaderOption
{
    public LoaderOption(LoaderKind kind, string name, string hint)
    {
        Kind = kind;
        Name = name;
        Hint = hint;
    }

    public LoaderKind Kind { get; }

    public string Name { get; }

    public string Hint { get; }
}

public sealed class LoaderInstallViewModel : ObservableObject
{
    private readonly LauncherContext _context;
    private readonly InstanceInfo? _target;
    private readonly Action<InstanceInfo> _installed;
    private LoaderOption _selectedLoader;
    private string? _selectedVersion;
    private bool _isBusy;
    private string _status = string.Empty;
    private bool _statusIsError;
    private double _progress;
    private string _progressText = string.Empty;

    public LoaderInstallViewModel(
        LauncherContext context,
        string minecraftVersion,
        InstanceInfo? target,
        Action<InstanceInfo> installed)
    {
        _context = context;
        _target = target;
        _installed = installed;
        MinecraftVersion = minecraftVersion;
        Loaders =
        [
            new(LoaderKind.Fabric, "Fabric", "轻量、更新快"),
            new(LoaderKind.Quilt, "Quilt", "Fabric 生态分支"),
            new(LoaderKind.Forge, "Forge", "老牌模组平台"),
            new(LoaderKind.NeoForge, "NeoForge", "Forge 社区分支")
        ];
        Versions = [];
        _selectedLoader = Loaders[0];
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy);
        ReloadCommand = new AsyncRelayCommand(ReloadAsync, () => !IsBusy);
        Status = "选择加载器后读取可用版本。";
        _ = ReloadAsync();
    }

    public string MinecraftVersion { get; }

    public IReadOnlyList<LoaderOption> Loaders { get; }

    public ObservableCollection<string> Versions { get; }

    public LoaderOption SelectedLoader
    {
        get => _selectedLoader;
        set
        {
            if (SetProperty(ref _selectedLoader, value))
            {
                _ = ReloadAsync();
            }
        }
    }

    public string? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
            {
                OnPropertyChanged(nameof(CanInstall));
                RefreshCommands();
            }
        }
    }

    public bool CanInstall => !IsBusy && SelectedVersion is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(IsIdle));
                RefreshCommands();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public ICommand InstallCommand { get; }

    public ICommand ReloadCommand { get; }

    private void RefreshCommands()
    {
        (InstallCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ReloadCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task ReloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = $"正在读取 {SelectedLoader.Name} 版本…";
        Versions.Clear();
        SelectedVersion = null;
        try
        {
            var versions = await _context.Loaders
                .GetLoaderVersionsAsync(SelectedLoader.Kind, MinecraftVersion)
                .ConfigureAwait(true);
            foreach (var version in versions)
            {
                Versions.Add(version);
            }

            SelectedVersion = Versions.FirstOrDefault();
            Status = Versions.Count == 0
                ? $"{SelectedLoader.Name} 没有适用于 {MinecraftVersion} 的版本。"
                : $"共 {Versions.Count} 个版本，默认选择最新版。";
            StatusIsError = Versions.Count == 0;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"读取版本失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanInstall));
        }
    }

    private async Task InstallAsync()
    {
        if (IsBusy || SelectedVersion is null)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Progress = 0;
        ProgressText = "准备安装…";
        var task = TaskCenter.Current.Begin(
            $"安装 {SelectedLoader.Name} {SelectedVersion}",
            $"{MinecraftVersion} · {SelectedLoader.Name}");
        try
        {
            InstanceInfo instance;
            if (_target is null)
            {
                instance = await _context.Instances.CreateAsync(
                        $"{MinecraftVersion} {SelectedLoader.Name}",
                        MinecraftVersion,
                        MinecraftVersion)
                    .ConfigureAwait(true);
            }
            else
            {
                instance = _target;
            }

            var progress = new Progress<DownloadProgress>(report =>
            {
                Progress = report.Percent;
                ProgressText = report.Item;
                task.Report(report);
            });
            await _context.Loaders.InstallAsync(
                    instance,
                    SelectedLoader.Kind,
                    SelectedVersion,
                    progress)
                .ConfigureAwait(true);
            Status = $"{SelectedLoader.Name} {SelectedVersion} 已安装。";
            Progress = 100;
            ProgressText = "安装完成";
            TaskCenter.Current.Complete(task, $"{SelectedLoader.Name} {SelectedVersion} 已安装");
            _installed(instance);
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"安装失败：{ex.Message}";
            ProgressText = "安装失败";
            TaskCenter.Current.Fail(task, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
