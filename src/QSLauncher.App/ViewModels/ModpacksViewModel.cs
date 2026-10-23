using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using QSLauncher.App.Services;
using QSLauncher.App.Views;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public sealed class ModpackHitItem : INotifyPropertyChanged
{
    private readonly HttpService _http;
    private bool _iconTried;
    private ImageSource? _icon;

    public ModpackHitItem(ModrinthSearchHit hit, HttpService http)
    {
        Hit = hit;
        _http = http;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ModrinthSearchHit Hit { get; }

    public string Title => Hit.Title;

    public string Description => DescriptionLocalizer.Summarize(
        Hit.Title, Hit.Description, Hit.Categories);

    public string OriginalDescription => Hit.Description;

    public string Author => $"by {Hit.Author}";

    public string IconUrl => Hit.IconUrl ?? string.Empty;

    public ImageSource? IconSource
    {
        get => _icon;
        private set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSource)));
        }
    }

    public async Task LoadIconAsync()
    {
        if (_iconTried || string.IsNullOrWhiteSpace(Hit.IconUrl))
        {
            return;
        }

        _iconTried = true;
        try
        {
            var bitmap = await WebpIconLoader.LoadAsync(_http, Hit.IconUrl, 96)
                .ConfigureAwait(false);
            if (bitmap is not null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => IconSource = bitmap);
            }
        }
        catch
        {
            // The text result must remain usable when an icon is unavailable.
        }
    }

    public string DownloadsText => Hit.Downloads switch
    {
        >= 1_000_000 => $"{Hit.Downloads / 1_000_000.0:0.#}M 下载",
        >= 1_000 => $"{Hit.Downloads / 1_000.0:0.#}K 下载",
        _ => $"{Hit.Downloads} 下载"
    };

    public string Tags => string.Join(" · ", Hit.Categories.Take(3));
}

public sealed class ModpackVersionItem
{
    public ModpackVersionItem(ModrinthVersion version)
    {
        Version = version;
    }

    public ModrinthVersion Version { get; }

    public string Name => string.IsNullOrWhiteSpace(Version.Name)
        ? Version.VersionNumber
        : Version.Name;

    public string VersionNumber => Version.VersionNumber;

    public string GameVersions => Version.GameVersions.Count == 0
        ? "未知版本"
        : string.Join(", ", Version.GameVersions.Take(3));

    public string Loaders => Version.Loaders.Count == 0
        ? "原版"
        : string.Join(" · ", Version.Loaders.Select(DisplayLoader));

    public string DateText => Version.DatePublished?.ToLocalTime().ToString("yyyy-MM-dd") ?? "—";

    public string SizeText
    {
        get
        {
            var size = Version.Files.FirstOrDefault(f => f.Primary)?.Size
                       ?? Version.Files.FirstOrDefault()?.Size
                       ?? 0;
            return size switch
            {
                >= 1024L * 1024 * 1024 => $"{size / 1024.0 / 1024 / 1024:0.#} GB",
                >= 1024L * 1024 => $"{size / 1024.0 / 1024:0.#} MB",
                _ => $"{size / 1024.0:0.#} KB"
            };
        }
    }

    private static string DisplayLoader(string loader) => loader.ToLowerInvariant() switch
    {
        "fabric" => "Fabric",
        "quilt" => "Quilt",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        _ => loader
    };
}

public sealed class ModpacksViewModel : ObservableObject
{
    private readonly LauncherContext _context;
    private readonly Action<PageKey> _navigate;
    private string _searchText = string.Empty;
    private string _status = "正在读取 Modrinth 整合包…";
    private bool _statusIsError;
    private bool _isBusy;
    private double _progress;
    private string _progressText = string.Empty;
    private ModpackHitItem? _selectedPack;
    private ModpackVersionItem? _selectedVersion;
    private string? _installedPath;
    private bool _pendingSearch;

    public ModpacksViewModel(LauncherContext context, Action<PageKey> navigate)
    {
        _context = context;
        _navigate = navigate;
        Results = [];
        Versions = [];
        SearchCommand = new AsyncRelayCommand(SearchAsync);
        InstallCommand = new AsyncRelayCommand(InstallAsync);
        ImportCommand = new AsyncRelayCommand(ImportAsync);
        OpenFolderCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrWhiteSpace(_installedPath))
            {
                OpenFolder(_installedPath);
            }
        });
        RefreshCommand = new AsyncRelayCommand(SearchAsync);
        _ = SearchAsync();
    }

    public ObservableCollection<ModpackHitItem> Results { get; }

    public ObservableCollection<ModpackVersionItem> Versions { get; }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
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

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
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

    public ModpackHitItem? SelectedPack
    {
        get => _selectedPack;
        set
        {
            if (SetProperty(ref _selectedPack, value))
            {
                OnPropertyChanged(nameof(HasSelectedPack));
                OnPropertyChanged(nameof(SelectedTitle));
                OnPropertyChanged(nameof(SelectedDescription));
                OnPropertyChanged(nameof(SelectedAuthor));
                OnPropertyChanged(nameof(SelectedDownloads));
                OnPropertyChanged(nameof(SelectedIcon));
                _ = LoadVersionsAsync(value);
            }
        }
    }

    public ModpackVersionItem? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
            {
                OnPropertyChanged(nameof(HasSelectedVersion));
                OnPropertyChanged(nameof(SelectedVersionText));
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    public bool HasSelectedPack => SelectedPack is not null;

    public bool HasSelectedVersion => SelectedVersion is not null;

    public bool CanInstall => HasSelectedPack && HasSelectedVersion && !IsBusy;

    public string SelectedTitle => SelectedPack?.Title ?? "选择一个整合包";

    public string SelectedDescription => SelectedPack?.Description ?? "从左侧挑选一个整合包，查看可用版本与加载器。";

    public string SelectedAuthor => SelectedPack?.Author ?? "Modrinth";

    public string SelectedDownloads => SelectedPack?.DownloadsText ?? string.Empty;

    public string SelectedIcon => SelectedPack?.IconUrl ?? string.Empty;

    public string SelectedVersionText => SelectedVersion is null
        ? "尚未选择版本"
        : $"{SelectedVersion.Name} · {SelectedVersion.Loaders} · {SelectedVersion.GameVersions}";

    public string InstalledStatus => string.IsNullOrWhiteSpace(_installedPath)
        ? string.Empty
        : "已安装到独立实例";

    public bool HasInstalledPath => !string.IsNullOrWhiteSpace(_installedPath);

    public ICommand SearchCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand OpenFolderCommand { get; }

    private async Task SearchAsync()
    {
        if (IsBusy)
        {
            _pendingSearch = true;
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = "正在按关联词搜索 Modrinth 整合包…";
        try
        {
            var terms = SearchTerms.Expand(SearchText);
            var perTerm = Math.Clamp(18 / terms.Count + 4, 6, 18);
            var batches = await Task.WhenAll(terms.Select(term => _context.Modrinth.SearchAsync(
                    term,
                    "modpack",
                    perTerm,
                    0)))
                .ConfigureAwait(true);

            var hits = SearchTerms.Merge(
                batches.Select(batch => (IReadOnlyList<ModrinthSearchHit>)batch.Hits),
                hit => hit.ProjectId,
                24);

            Results.Clear();
            foreach (var hit in hits)
            {
                var item = new ModpackHitItem(hit, _context.Http);
                Results.Add(item);
                _ = item.LoadIconAsync();
            }

            SelectedPack = Results.FirstOrDefault();
            var total = batches.Sum(batch => batch.TotalHits);
            Status = Results.Count == 0
                ? $"没有找到与“{SearchText}”相关的整合包。"
                : terms.Count > 1
                    ? $"已按 {terms.Count} 个关联词检索 · 显示前 {Results.Count} 个"
                    : $"找到 {total} 个整合包 · 显示前 {Results.Count} 个";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"Modrinth 搜索失败：{ex.Message}";
            AppLog.Error("整合包搜索失败", ex);
        }
        finally
        {
            IsBusy = false;
            if (_pendingSearch)
            {
                _pendingSearch = false;
                _ = SearchAsync();
            }
        }
    }

    private async Task LoadVersionsAsync(ModpackHitItem? pack)
    {
        if (pack is null)
        {
            Versions.Clear();
            SelectedVersion = null;
            return;
        }

        try
        {
            var versions = await _context.Modrinth
                .GetVersionsAsync(pack.Hit.ProjectId)
                .ConfigureAwait(true);

            Versions.Clear();
            foreach (var version in versions.Take(20))
            {
                Versions.Add(new ModpackVersionItem(version));
            }

            SelectedVersion = Versions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"版本读取失败：{ex.Message}";
            AppLog.Error("整合包版本读取失败", ex);
        }
    }

    private async Task InstallAsync()
    {
        if (SelectedPack is null || SelectedVersion is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Progress = 0;
        ProgressText = "准备安装整合包…";
        Status = $"正在安装 {SelectedPack.Title}";
        _installedPath = null;
        OnPropertyChanged(nameof(HasInstalledPath));
        OnPropertyChanged(nameof(InstalledStatus));

        var task = TaskCenter.Current.Begin($"安装整合包 {SelectedPack.Title}", SelectedVersion.Name);
        try
        {
            var progress = new Progress<DownloadProgress>(report =>
            {
                Progress = report.Percent;
                ProgressText = report.Item;
                task.Report(report);
            });

            var instance = await _context.Modpacks.InstallFromVersionAsync(
                SelectedVersion.Version,
                SelectedPack.Hit.ProjectId,
                progress).ConfigureAwait(true);

            _installedPath = instance.Directory;
            OnPropertyChanged(nameof(HasInstalledPath));
            OnPropertyChanged(nameof(InstalledStatus));
            Status = $"{SelectedPack.Title} 已安装为独立实例，可在“版本管理”中启动。";
            Progress = 100;
            ProgressText = "整合包安装完成";
            TaskCenter.Current.Complete(task, "整合包已安装为独立实例");
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"整合包安装失败：{ex.Message}";
            ProgressText = "安装失败";
            TaskCenter.Current.Fail(task, ex.Message);
            AppLog.Error("整合包安装失败", ex);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanInstall));
        }
    }

    private async Task ImportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var archivePath = FileBrowserWindow.PickFile(
            "导入本地整合包",
            "整合包 (*.mrpack;*.zip)|*.mrpack;*.zip|所有文件 (*.*)|*.*");
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return;
        }

        string? fallbackVersion = null;
        var fallbackLoader = LoaderKind.Vanilla;
        if (ModpackInstaller.NeedsFallback(archivePath))
        {
            var picker = new ModpackFallbackWindow(archivePath);
            if (Application.Current?.MainWindow is { IsLoaded: true } owner)
            {
                picker.Owner = owner;
            }

            if (picker.ShowDialog() != true)
            {
                return;
            }

            fallbackVersion = picker.MinecraftVersion;
            fallbackLoader = picker.Loader;
        }

        IsBusy = true;
        StatusIsError = false;
        Progress = 0;
        ProgressText = "读取本地整合包…";
        Status = "正在导入本地整合包";
        var task = TaskCenter.Current.Begin(
            $"导入整合包 {Path.GetFileName(archivePath)}",
            "读取整合包清单…");
        try
        {
            var progress = new Progress<DownloadProgress>(report =>
            {
                Progress = report.Percent;
                ProgressText = report.Item;
                task.Report(report);
            });
            var instance = await _context.Modpacks.InstallFromFileAsync(
                    archivePath,
                    progress: progress,
                    fallbackMinecraftVersion: fallbackVersion,
                    fallbackLoader: fallbackLoader)
                .ConfigureAwait(true);

            _installedPath = instance.Directory;
            OnPropertyChanged(nameof(HasInstalledPath));
            OnPropertyChanged(nameof(InstalledStatus));
            Status = $"{instance.DisplayName} 导入完成，可在“版本管理”中启动。";
            Progress = 100;
            ProgressText = "导入完成";
            TaskCenter.Current.Complete(task, "本地整合包已导入");
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"导入失败：{ex.Message}";
            ProgressText = "导入失败";
            TaskCenter.Current.Fail(task, ex.Message);
            AppLog.Error("本地整合包导入失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
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
