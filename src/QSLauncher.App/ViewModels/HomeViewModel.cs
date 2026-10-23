using System.IO;
using System.Windows.Input;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public sealed class HomeWidgetOption : ObservableObject
{
    private readonly HomeWidgetSetting _setting;
    private readonly Action _changed;

    public HomeWidgetOption(string key, string title, HomeWidgetSetting setting, Action changed)
    {
        Key = key;
        Title = title;
        _setting = setting;
        _changed = changed;
    }

    public string Key { get; }

    public string Title { get; }

    public double X
    {
        get => _setting.X;
        set => SetLayout(_setting.X, value, v => _setting.X = Math.Clamp(v, 0, 2600));
    }

    public double Y
    {
        get => _setting.Y;
        set => SetLayout(_setting.Y, value, v => _setting.Y = Math.Clamp(v, 0, 2600));
    }

    public double Width
    {
        get => _setting.Width;
        set => SetLayout(_setting.Width, value, v => _setting.Width = Math.Clamp(v, 260, 2200));
    }

    public double Height
    {
        get => _setting.Height;
        set => SetLayout(_setting.Height, value, v => _setting.Height = Math.Clamp(v, 100, 1600));
    }

    public double ContentWidth => Math.Max(120, Width - 60);

    public bool Visible
    {
        get => _setting.Visible;
        set
        {
            if (_setting.Visible == value)
            {
                return;
            }

            _setting.Visible = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(ContentWidth));
        OnPropertyChanged(nameof(Visible));
    }

    private void SetLayout(double current, double value, Action<double> apply)
    {
        var clamped = Math.Clamp(value, 0, 2600);
        if (Math.Abs(current - clamped) < 0.01)
        {
            return;
        }

        apply(clamped);
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(ContentWidth));
        _changed();
    }
}

public sealed class HomeViewModel : ObservableObject
{
    private readonly PortablePaths _paths;
    private readonly VersionsViewModel _versions;
    private readonly Action<PageKey> _navigate;
    private readonly SettingsService _settings;
    private int _instanceCount;
    private int _modCount;
    private int _javaCount;
    private string _diskUsageText = "0 MB";
    private string _greeting = "你好";
    private bool _isEditingLayout;

    public HomeViewModel(
        PortablePaths paths,
        VersionsViewModel versions,
        Action<PageKey> navigate,
        SettingsService settings)
    {
        _paths = paths;
        _versions = versions;
        _navigate = navigate;
        _settings = settings;
        LaunchWidget = new HomeWidgetOption(
            HomeWidgetKeys.Launch, "启动实例",
            HomeLayoutDefaults.Get(settings.Current.Home, HomeWidgetKeys.Launch), NotifyLayoutChanged);
        QuickWidget = new HomeWidgetOption(
            HomeWidgetKeys.Quick, "快捷操作",
            HomeLayoutDefaults.Get(settings.Current.Home, HomeWidgetKeys.Quick), NotifyLayoutChanged);
        StatsWidget = new HomeWidgetOption(
            HomeWidgetKeys.Stats, "实例统计",
            HomeLayoutDefaults.Get(settings.Current.Home, HomeWidgetKeys.Stats), NotifyLayoutChanged);
        RecentWidget = new HomeWidgetOption(
            HomeWidgetKeys.Recent, "最近实例",
            HomeLayoutDefaults.Get(settings.Current.Home, HomeWidgetKeys.Recent), NotifyLayoutChanged);
        _versions.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VersionsViewModel.SelectedInstance))
            {
                ModCount = CountMods();
                OnPropertyChanged(nameof(ModCountText));
                OnPropertyChanged(nameof(ModCountHint));
            }
        };
        ToggleLayoutCommand = new RelayCommand(() => IsEditingLayout = !IsEditingLayout);
        ResetLayoutCommand = new RelayCommand(ResetLayout);
        settings.Changed += (_, _) => RefreshLayout();

        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is string text && Enum.TryParse<PageKey>(text, ignoreCase: true, out var key))
            {
                _navigate(key);
            }
        });
        PrimaryActionCommand = new RelayCommand(() => _navigate(PageKey.Downloads));
        InstallModpackCommand = new RelayCommand(() => _navigate(PageKey.Modpacks));
        ManageJavaCommand = new RelayCommand(() => _navigate(PageKey.Java));
        OpenSettingsCommand = new RelayCommand(() => _navigate(PageKey.Settings));
        Refresh();
    }

    public VersionsViewModel Versions => _versions;

    public HomeWidgetOption LaunchWidget { get; }

    public HomeWidgetOption QuickWidget { get; }

    public HomeWidgetOption StatsWidget { get; }

    public HomeWidgetOption RecentWidget { get; }

    public bool IsEditingLayout
    {
        get => _isEditingLayout;
        private set
        {
            if (SetProperty(ref _isEditingLayout, value))
            {
                OnPropertyChanged(nameof(LayoutButtonText));
                OnPropertyChanged(nameof(LayoutHint));
            }
        }
    }

    public string LayoutButtonText => IsEditingLayout ? "完成布局" : "编辑布局";

    public string LayoutHint => IsEditingLayout
        ? "拖动顶部把手移动控件，拖动右下角缩放；设置页可隐藏控件。"
        : "主页控件支持拖动、缩放与隐藏。";

    public double LayoutWidth
    {
        get
        {
            var visible = AllWidgets().Where(widget => widget.Visible).ToList();
            return Math.Max(880, visible.Count == 0 ? 0 : visible.Max(widget => widget.X + widget.Width) + 12);
        }
    }

    public double LayoutHeight
    {
        get
        {
            var visible = AllWidgets().Where(widget => widget.Visible).ToList();
            return Math.Max(620, visible.Count == 0 ? 0 : visible.Max(widget => widget.Y + widget.Height) + 12);
        }
    }

    public ICommand ToggleLayoutCommand { get; }

    public ICommand ResetLayoutCommand { get; }

    public string Greeting
    {
        get => _greeting;
        private set => SetProperty(ref _greeting, value);
    }

    public string TodayText => DateTime.Now.ToString("yyyy 年 M 月 d 日 dddd");

    public int InstanceCount
    {
        get => _instanceCount;
        private set => SetProperty(ref _instanceCount, value);
    }

    public int ModCount
    {
        get => _modCount;
        private set => SetProperty(ref _modCount, value);
    }

    public int JavaCount
    {
        get => _javaCount;
        private set => SetProperty(ref _javaCount, value);
    }

    public string DiskUsageText
    {
        get => _diskUsageText;
        private set => SetProperty(ref _diskUsageText, value);
    }

    public bool HasInstances => InstanceCount > 0;

    public string InstanceCountText => InstanceCount.ToString();

    public string ModCountText => ModCount.ToString();

    public string ModCountHint => _versions.SelectedInstance is { } instance
        ? $"当前实例 · {instance.Name}"
        : "当前未选择实例";

    public string JavaCountText => JavaCount.ToString();

    public string InstanceSummary => HasInstances ? $"{InstanceCount} 个实例已就绪" : "还没有安装任何实例";

    public string PrimaryActionLabel => HasInstances ? "查看我的实例" : "安装第一个版本";

    public string PrimaryActionHint => HasInstances
        ? "管理版本、模组与启动参数"
        : "从 Mojang 官方源下载，完全便携";

    public string EmptyTitle => HasInstances ? "最近启动" : "你的冒险还没有开始";

    public string EmptyDescription => HasInstances
        ? "启动记录会显示在这里。"
        : "先安装一个官方版本，或者导入一个整合包。";

    public ICommand NavigateCommand { get; }

    public ICommand PrimaryActionCommand { get; }

    public ICommand InstallModpackCommand { get; }

    public ICommand ManageJavaCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public void Refresh()
    {
        Greeting = DateTime.Now.Hour switch
        {
            < 6 => "夜深了",
            < 11 => "早上好",
            < 14 => "中午好",
            < 18 => "下午好",
            _ => "晚上好"
        };

        InstanceCount = CountInstances();
        ModCount = CountMods();
        JavaCount = CountJavaRuntimes();
        DiskUsageText = FormatBytes(CalculateSize(_paths.DataRoot));

        OnPropertyChanged(nameof(HasInstances));
        OnPropertyChanged(nameof(InstanceCountText));
        OnPropertyChanged(nameof(ModCountText));
        OnPropertyChanged(nameof(ModCountHint));
        OnPropertyChanged(nameof(JavaCountText));
        OnPropertyChanged(nameof(InstanceSummary));
        OnPropertyChanged(nameof(PrimaryActionLabel));
        OnPropertyChanged(nameof(PrimaryActionHint));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(TodayText));
    }

    public async Task SaveLayoutAsync()
    {
        try
        {
            await _settings.SaveAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Error("保存主页布局失败", ex);
        }
    }

    public void RefreshLayout()
    {
        foreach (var widget in AllWidgets())
        {
            // The option wraps the existing setting object; refresh values in place.
            widget.Refresh();
        }

        OnPropertyChanged(nameof(LayoutWidth));
        OnPropertyChanged(nameof(LayoutHeight));
    }

    private IEnumerable<HomeWidgetOption> AllWidgets()
    {
        yield return LaunchWidget;
        yield return QuickWidget;
        yield return StatsWidget;
        yield return RecentWidget;
    }

    private void NotifyLayoutChanged()
    {
        OnPropertyChanged(nameof(LayoutWidth));
        OnPropertyChanged(nameof(LayoutHeight));
    }

    private void ResetLayout()
    {
        var defaults = HomeLayoutDefaults.Create();
        foreach (var widget in AllWidgets())
        {
            var target = defaults.Widgets.First(item => item.Key == widget.Key);
            widget.X = target.X;
            widget.Y = target.Y;
            widget.Width = target.Width;
            widget.Height = target.Height;
            widget.Visible = true;
        }

        NotifyLayoutChanged();
        _ = SaveLayoutAsync();
    }

    private int CountInstances()
    {
        try
        {
            return Directory.Exists(_paths.InstancesPath)
                ? Directory.GetDirectories(_paths.InstancesPath)
                    .Count(dir => File.Exists(Path.Combine(dir, "instance.json")))
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private int CountMods()
    {
        try
        {
            var modsDirectory = _versions.SelectedInstance?.Info.ModsDirectory;
            if (string.IsNullOrWhiteSpace(modsDirectory) || !Directory.Exists(modsDirectory))
            {
                return 0;
            }

            return Directory.GetFiles(modsDirectory, "*.jar*", SearchOption.TopDirectoryOnly).Length;
        }
        catch
        {
            return 0;
        }
    }

    private int CountJavaRuntimes()
    {
        try
        {
            if (!Directory.Exists(_paths.JavaPath))
            {
                return 0;
            }

            return Directory.GetDirectories(_paths.JavaPath)
                .Count(dir => File.Exists(Path.Combine(dir, "bin", "java.exe")));
        }
        catch
        {
            return 0;
        }
    }

    private static long CalculateSize(string root)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return 0;
            }

            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try
                    {
                        return new FileInfo(file).Length;
                    }
                    catch
                    {
                        return 0L;
                    }
                });
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }
}
