using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using QSLauncher.App.Services;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public sealed class JavaRuntimeItem : ObservableObject
{
    private bool _isDefault;

    public JavaRuntimeItem(JavaRuntimeInfo info)
    {
        Info = info;
    }

    public JavaRuntimeInfo Info { get; }

    public string Name => $"Java {Info.MajorVersion}";

    public string VersionText => string.IsNullOrWhiteSpace(Info.VersionString)
        ? Info.DisplayName
        : Info.VersionString;

    public string Vendor => string.IsNullOrWhiteSpace(Info.Vendor) ? "未知厂商" : Info.Vendor;

    public string Architecture => Info.Architecture;

    public string SourceText => Info.IsPortable ? "便携运行时" : "系统安装";

    public string Path => Info.ExecutablePath;

    public bool IsRecommended => Info.MajorVersion >= 25;

    public bool IsDefault
    {
        get => _isDefault;
        set => SetProperty(ref _isDefault, value);
    }
}

public sealed class JavaViewModel : ObservableObject
{
    private static readonly Dictionary<int, string> OfficialComponents = new()
    {
        [8] = "jre-legacy",
        [16] = "java-runtime-alpha",
        [17] = "java-runtime-beta",
        [21] = "java-runtime-delta",
        [25] = "java-runtime-epsilon"
    };

    private readonly LauncherContext _context;
    private readonly SettingsService _settings;
    private readonly Action<PageKey> _navigate;
    private bool _isBusy;
    private string _status = "正在扫描本机 Java…";
    private bool _statusIsError;
    private double _progress;
    private string _progressText = string.Empty;
    private JavaRuntimeItem? _selectedRuntime;

    public JavaViewModel(
        LauncherContext context,
        SettingsService settings,
        Action<PageKey> navigate)
    {
        _context = context;
        _settings = settings;
        _navigate = navigate;
        Runtimes = [];
        ScanCommand = new AsyncRelayCommand(ScanAsync);
        InstallCommand = new AsyncRelayCommand(InstallAsync);
        SetDefaultCommand = new RelayCommand(SetDefault);
        OpenFolderCommand = new RelayCommand(() => OpenFolder(_context.Paths.JavaPath));
        _ = ScanAsync();
    }

    public ObservableCollection<JavaRuntimeItem> Runtimes { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

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

    public JavaRuntimeItem? SelectedRuntime
    {
        get => _selectedRuntime;
        set => SetProperty(ref _selectedRuntime, value);
    }

    public bool HasSelectedRuntime => SelectedRuntime is not null;

    public bool HasRuntimes => Runtimes.Count > 0;

    public string DefaultRuntimeText
    {
        get
        {
            var defaultId = _settings.Current.Java.DefaultRuntimeId;
            var match = Runtimes.FirstOrDefault(r =>
                string.Equals(r.Info.Id, defaultId, StringComparison.OrdinalIgnoreCase));
            return match is null ? "自动选择（按游戏版本）" : $"{match.Name} · {match.VersionText}";
        }
    }

    public string RuntimeCountText => $"{Runtimes.Count} 个可用运行时";

    public ICommand ScanCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand SetDefaultCommand { get; }

    public ICommand OpenFolderCommand { get; }

    private async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = "正在扫描 Java…";
        try
        {
            var runtimes = await _context.Java.ScanAsync(refresh: true).ConfigureAwait(true);
            Runtimes.Clear();
            foreach (var runtime in runtimes)
            {
                Runtimes.Add(new JavaRuntimeItem(runtime));
            }

            RefreshDefaultFlags();
            SelectedRuntime ??= Runtimes.FirstOrDefault(r => r.IsDefault) ?? Runtimes.FirstOrDefault();
            OnPropertyChanged(nameof(DefaultRuntimeText));
            OnPropertyChanged(nameof(RuntimeCountText));
            OnPropertyChanged(nameof(HasRuntimes));
            Status = Runtimes.Count == 0
                ? "没有找到 Java，可安装官方运行时。"
                : $"已检测到 {Runtimes.Count} 个 Java 运行时。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"Java 扫描失败：{ex.Message}";
            AppLog.Error("Java 扫描失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallAsync(object? parameter)
    {
        if (IsBusy || parameter is not string majorText || !int.TryParse(majorText, out var major))
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Progress = 0;
        ProgressText = $"准备安装 Java {major}…";
        Status = $"正在安装 Java {major} 官方运行时";
        var task = TaskCenter.Current.Begin($"安装 Java {major}", "获取官方运行时清单…");
        try
        {
            var component = OfficialComponents.GetValueOrDefault(major);
            var progress = new Progress<DownloadProgress>(report =>
            {
                Progress = report.Percent;
                ProgressText = report.Item;
                task.Report(report);
            });
            var runtime = await _context.Java.InstallOfficialRuntimeAsync(
                component,
                major,
                progress).ConfigureAwait(true);

            IsBusy = false;
            await ScanAsync().ConfigureAwait(true);
            SelectedRuntime = Runtimes.FirstOrDefault(r =>
                                 r.Info.ExecutablePath.Equals(runtime.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                             ?? Runtimes.FirstOrDefault();
            Status = $"Java {runtime.MajorVersion} 已安装并可用于启动。";
            Progress = 100;
            ProgressText = "安装完成";
            TaskCenter.Current.Complete(task, $"Java {runtime.MajorVersion} 已可用");
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"Java 安装失败：{ex.Message}";
            ProgressText = "安装失败";
            TaskCenter.Current.Fail(task, ex.Message);
            AppLog.Error($"Java {major} 安装失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetDefault(object? parameter)
    {
        var item = parameter as JavaRuntimeItem ?? SelectedRuntime;
        if (item is null)
        {
            return;
        }

        _settings.Current.Java.DefaultRuntimeId = item.Info.Id;
        _ = _settings.SaveAsync();
        RefreshDefaultFlags();
        SelectedRuntime = item;
        OnPropertyChanged(nameof(DefaultRuntimeText));
        Status = $"默认 Java 已设为 {item.Name} · {item.VersionText}";
    }

    private void RefreshDefaultFlags()
    {
        var defaultId = _settings.Current.Java.DefaultRuntimeId;
        foreach (var runtime in Runtimes)
        {
            runtime.IsDefault = string.Equals(runtime.Info.Id, defaultId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell errors.
        }
    }
}
