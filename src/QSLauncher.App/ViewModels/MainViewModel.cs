using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using QSLauncher.App.Services;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public enum PageKey
{
    Home,
    Downloads,
    Tasks,
    Logs,
    Versions,
    Modpacks,
    Mods,
    Java,
    Resources,
    Settings,
    Account
}

public sealed class NavigationItem : ObservableObject
{
    private bool _isCurrent;

    public NavigationItem(PageKey key, string title, string iconKey)
    {
        Key = key;
        Title = title;
        Icon = Application.Current.TryFindResource(iconKey) as Geometry ?? Geometry.Empty;
    }

    public PageKey Key { get; }

    public string Title { get; }

    public Geometry Icon { get; }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly Dictionary<PageKey, object> _pages;
    private NavigationItem? _selectedNavigation;
    private object _currentPage;
    private bool _isSidebarCollapsed;

    public MainViewModel(
        PortablePaths paths,
        SettingsService settings,
        ThemeService theme,
        BackgroundService background)
    {
        Background = background;
        Launcher = new LauncherContext(paths);
        Accounts = new AccountsViewModel(paths, Launcher, settings, Navigate);
        Accounts.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(AccountsViewModel.SelectedAccount)
                or nameof(AccountsViewModel.CurrentAccount))
            {
                OnPropertyChanged(nameof(AccountName));
                OnPropertyChanged(nameof(AccountDetail));
            }
        };
        LogCenter = new LogCenter(paths.LogsPath);
        Versions = new VersionsViewModel(
            paths,
            Launcher,
            settings,
            () => Accounts.GetLaunchAccountAsync(),
            LogCenter);
        Home = new HomeViewModel(paths, Versions, Navigate, settings);
        Downloads = new DownloadsViewModel(Versions);
        Tasks = new TasksViewModel(TaskCenter.Current);
        Logs = new LogsViewModel(LogCenter, paths);
        Modpacks = new ModpacksViewModel(Launcher, Navigate);
        Mods = new ModsViewModel(Launcher, settings, Navigate);
        Java = new JavaViewModel(Launcher, settings, Navigate);
        Resources = new ResourcesViewModel(Launcher, Navigate);
        SettingsPage = new SettingsViewModel(paths, settings, theme, background);

        _pages = new Dictionary<PageKey, object>
        {
            [PageKey.Home] = Home,
            [PageKey.Downloads] = Downloads,
            [PageKey.Tasks] = Tasks,
            [PageKey.Logs] = Logs,
            [PageKey.Versions] = Versions,
            [PageKey.Modpacks] = Modpacks,
            [PageKey.Mods] = Mods,
            [PageKey.Java] = Java,
            [PageKey.Resources] = Resources,
            [PageKey.Settings] = SettingsPage,
            [PageKey.Account] = Accounts
        };

        NavigationItems = new ObservableCollection<NavigationItem>
        {
            new(PageKey.Home, "首页", "IconHome"),
            new(PageKey.Downloads, "下载", "IconDownload"),
            new(PageKey.Tasks, "任务", "IconTasks"),
            new(PageKey.Logs, "日志", "IconLog"),
            new(PageKey.Versions, "版本管理", "IconLayers"),
            new(PageKey.Modpacks, "整合包", "IconPackage"),
            new(PageKey.Mods, "模组", "IconPuzzle"),
            new(PageKey.Java, "Java", "IconJava"),
            new(PageKey.Resources, "资源", "IconFolder"),
            new(PageKey.Settings, "设置", "IconSettings")
        };

        _currentPage = Home;
        _selectedNavigation = NavigationItems[0];
        NavigationItems[0].IsCurrent = true;

        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is PageKey key)
            {
                Navigate(key);
            }
            else if (parameter is string text && Enum.TryParse<PageKey>(text, ignoreCase: true, out var parsed))
            {
                Navigate(parsed);
            }
        });

        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(paths.DataRoot));
        OpenBackgroundsFolderCommand = new RelayCommand(() => OpenFolder(paths.BackgroundsPath));
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public NavigationItem? SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (value is not null)
            {
                Navigate(value.Key);
            }
        }
    }

    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public LauncherContext Launcher { get; }

    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set
        {
            if (SetProperty(ref _isSidebarCollapsed, value))
            {
                OnPropertyChanged(nameof(SidebarWidth));
                OnPropertyChanged(nameof(SidebarToggleGlyph));
                OnPropertyChanged(nameof(SidebarToggleHint));
            }
        }
    }

    public GridLength SidebarWidth => IsSidebarCollapsed
        ? new GridLength(72)
        : new GridLength(228);

    public string SidebarToggleGlyph => IsSidebarCollapsed ? "›" : "‹";

    public string SidebarToggleHint => IsSidebarCollapsed ? "展开侧边栏" : "折叠侧边栏";

    public ICommand ToggleSidebarCommand { get; }

    public HomeViewModel Home { get; }

    public DownloadsViewModel Downloads { get; }

    public TasksViewModel Tasks { get; }

    public LogsViewModel Logs { get; }

    public LogCenter LogCenter { get; }

    public AccountsViewModel Accounts { get; }

    public VersionsViewModel Versions { get; }

    public ModpacksViewModel Modpacks { get; }

    public ModsViewModel Mods { get; }

    public JavaViewModel Java { get; }

    public ResourcesViewModel Resources { get; }

    public SettingsViewModel SettingsPage { get; }

    public BackgroundService Background { get; }

    public ICommand NavigateCommand { get; }

    public ICommand OpenDataFolderCommand { get; }

    public ICommand OpenBackgroundsFolderCommand { get; }

    public string AccountName => Accounts.SelectedAccount?.Name ?? "离线账户";

    public string AccountDetail => Accounts.SelectedAccount?.KindText ?? "本地模式";

    public string VersionText => AppInfo.Version;

    public void Navigate(PageKey key)
    {
        if (!_pages.ContainsKey(key))
        {
            return;
        }

        var item = NavigationItems.FirstOrDefault(x => x.Key == key);
        foreach (var navigation in NavigationItems)
        {
            navigation.IsCurrent = ReferenceEquals(navigation, item);
        }

        if (item is not null)
        {
            _selectedNavigation = item;
        }

        OnPropertyChanged(nameof(SelectedNavigation));
        CurrentPage = _pages[key];

        switch (CurrentPage)
        {
            case HomeViewModel home:
                home.Refresh();
                break;
            case DownloadsViewModel downloads:
                _ = downloads.Core.RefreshAsync();
                break;
            case TasksViewModel tasks:
                tasks.Refresh();
                break;
            case LogsViewModel logs:
                logs.RefreshCommand.Execute(null);
                break;
            case VersionsViewModel versions:
                _ = versions.RefreshAsync();
                break;
            case ModsViewModel mods:
                _ = mods.RefreshAsync();
                break;
            case ResourcesViewModel resources:
                _ = resources.RefreshAsync();
                break;
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
            // Opening a folder is a convenience, never a fatal action.
        }
    }
}
