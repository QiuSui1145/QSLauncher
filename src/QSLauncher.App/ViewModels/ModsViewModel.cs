using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using QSLauncher.App.Services;
using QSLauncher.App.Views;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public sealed class InstalledModItem
{
    public InstalledModItem(InstalledMod mod)
    {
        Mod = mod;
    }

    public InstalledMod Mod { get; }

    public string Name => Mod.DisplayName;

    public string FileName => Mod.FileName;

    public string SizeText => Mod.Size >= 1024 * 1024
        ? $"{Mod.Size / 1024d / 1024d:0.0} MB"
        : $"{Math.Max(1, Mod.Size / 1024)} KB";

    public string StateText => Mod.IsEnabled ? "已启用" : "已禁用";

    public string VersionText => string.IsNullOrWhiteSpace(Mod.VersionNumber)
        ? "本地文件"
        : $"v{Mod.VersionNumber}";

    public string ToggleText => Mod.IsEnabled ? "禁用" : "启用";
}

public sealed record ModFilterOption(string Value, string Label);

public sealed class ModSearchItem : System.ComponentModel.INotifyPropertyChanged
{
    private readonly HttpService _http;
    private bool _iconTried;
    private ImageSource? _icon;

    public ModSearchItem(ModrinthSearchHit hit, HttpService http)
    {
        Hit = hit;
        _http = http;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public ModrinthSearchHit Hit { get; }

    public string Title => Hit.Title;

    public string Description => DescriptionLocalizer.Summarize(
        Hit.Title, Hit.Description, Hit.Categories);

    public string OriginalDescription => Hit.Description;

    public bool HasOriginalDescription => !string.Equals(
        Description, Hit.Description, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(Hit.Description);

    public ImageSource? IconSource
    {
        get => _icon;
        private set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IconSource)));
        }
    }

    /// <summary>
    /// Modrinth serves icons as WebP. WPF has no built-in WebP decoder, so the
    /// bytes are decoded off the UI thread and converted to a frozen PNG frame.
    /// </summary>
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
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => IconSource = bitmap);
            }
        }
        catch
        {
            // A missing icon must never break the search result list.
        }
    }

    public string DownloadsText => Hit.Downloads >= 1_000_000
        ? $"{Hit.Downloads / 1_000_000d:0.0}M 下载"
        : Hit.Downloads >= 1000
            ? $"{Hit.Downloads / 1000d:0.0}K 下载"
            : $"{Hit.Downloads} 下载";

    public string Initial => string.IsNullOrWhiteSpace(Hit.Title)
        ? "?"
        : Hit.Title[..1].ToUpperInvariant();
}

public sealed class ModUpdateItem
{
    public ModUpdateItem(ModUpdate update)
    {
        Update = update;
    }

    public ModUpdate Update { get; }

    public string Name => Update.Name;

    public string VersionText => $"→ {Update.LatestVersionNumber}";
}

public sealed class ModsViewModel : ObservableObject
{
    private const int InstalledPageSize = 8;
    private const int ResultsPageSize = 8;
    private readonly LauncherContext _context;
    private readonly Action<PageKey> _navigate;
    private readonly List<InstalledModItem> _allInstalledMods = [];
    private readonly List<ModSearchItem> _allResults = [];
    private int _installedPage = 1;
    private int _resultsPage = 1;
    private InstanceItem? _selectedInstance;
    private string _searchText = string.Empty;
    private string _status = "选择一个实例以管理模组。";
    private bool _statusIsError;
    private bool _isBusy;
    private double _progress;
    private string _progressText = string.Empty;
    private bool _hasSearched;
    private string _selectedCategory = string.Empty;
    private string _selectedGameVersion = string.Empty;
    private string _selectedLoader = string.Empty;
    private bool _isRecommended;
    private bool _pendingSearch;
    private readonly SettingsService _settings;

    public ModsViewModel(LauncherContext context, SettingsService settings, Action<PageKey> navigate)
    {
        _settings = settings;
        _context = context;
        _navigate = navigate;
        Instances = [];
        InstalledMods = [];
        Results = [];
        Updates = [];
        Categories =
        [
            new ModFilterOption(string.Empty, "全部分类"),
            new ModFilterOption("optimization", "优化"),
            new ModFilterOption("technology", "科技"),
            new ModFilterOption("magic", "魔法"),
            new ModFilterOption("adventure", "冒险"),
            new ModFilterOption("decoration", "装饰"),
            new ModFilterOption("worldgen", "世界生成"),
            new ModFilterOption("library", "前置库"),
            new ModFilterOption("equipment", "装备"),
            new ModFilterOption("food", "食物"),
            new ModFilterOption("transportation", "运输")
        ];
        GameVersions =
        [
            new ModFilterOption(string.Empty, "跟随实例版本"),
            new ModFilterOption("26.3", "26.3"),
            new ModFilterOption("1.21.1", "1.21.1"),
            new ModFilterOption("1.20.1", "1.20.1"),
            new ModFilterOption("1.19.2", "1.19.2"),
            new ModFilterOption("1.18.2", "1.18.2"),
            new ModFilterOption("1.16.5", "1.16.5"),
            new ModFilterOption("1.12.2", "1.12.2"),
            new ModFilterOption("1.7.10", "1.7.10")
        ];
        Loaders =
        [
            new ModFilterOption(string.Empty, "跟随实例加载器"),
            new ModFilterOption("fabric", "Fabric"),
            new ModFilterOption("quilt", "Quilt"),
            new ModFilterOption("forge", "Forge"),
            new ModFilterOption("neoforge", "NeoForge")
        ];
        SearchCommand = new AsyncRelayCommand(SearchFromQueryAsync);
        RecommendCommand = new AsyncRelayCommand(RecommendAsync);
        SelectCategoryCommand = new AsyncRelayCommand(SelectCategoryAsync);
        InstallCommand = new AsyncRelayCommand(InstallAsync);
        ToggleCommand = new AsyncRelayCommand(ToggleAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        ImportCommand = new AsyncRelayCommand(ImportAsync);
        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync);
        ApplyUpdateCommand = new AsyncRelayCommand(ApplyUpdateAsync);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        InstalledPreviousPageCommand = new RelayCommand(
            () => SetInstalledPage(_installedPage - 1));
        InstalledNextPageCommand = new RelayCommand(
            () => SetInstalledPage(_installedPage + 1));
        ResultsPreviousPageCommand = new RelayCommand(
            () => SetResultsPage(_resultsPage - 1));
        ResultsNextPageCommand = new RelayCommand(
            () => SetResultsPage(_resultsPage + 1));
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        _ = LoadGameVersionsAsync();
        _ = LoadAsync();
    }

    private async Task LoadGameVersionsAsync()
    {
        try
        {
            var manifest = await _context.Mojang.GetManifestAsync().ConfigureAwait(true);
            var current = SelectedGameVersion;
            var options = new List<ModFilterOption>
            {
                new(string.Empty, "跟随实例版本")
            };
            options.AddRange(manifest.Versions.Select(version =>
                new ModFilterOption(
                    version.Id,
                    version.Type == "release" ? version.Id : $"{version.Id}（快照）")));
            GameVersions.Clear();
            foreach (var option in options)
            {
                GameVersions.Add(option);
            }

            if (!string.IsNullOrWhiteSpace(current)
                && GameVersions.Any(option => option.Value == current))
            {
                SelectedGameVersion = current;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"读取官方版本清单失败，保留内置版本筛选：{ex.Message}");
        }
    }

    public Task RefreshAsync() => LoadAsync();

    public ObservableCollection<InstanceItem> Instances { get; }

    public ObservableCollection<InstalledModItem> InstalledMods { get; }

    public ObservableCollection<ModSearchItem> Results { get; }

    public ObservableCollection<ModUpdateItem> Updates { get; }

    public ObservableCollection<ModFilterOption> Categories { get; }

    public ObservableCollection<ModFilterOption> GameVersions { get; }

    public ObservableCollection<ModFilterOption> Loaders { get; }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set => SetProperty(ref _selectedCategory, value);
    }

    public string SelectedGameVersion
    {
        get => _selectedGameVersion;
        set => SetProperty(ref _selectedGameVersion, value);
    }

    public string SelectedLoader
    {
        get => _selectedLoader;
        set => SetProperty(ref _selectedLoader, value);
    }

    public bool IsRecommended
    {
        get => _isRecommended;
        private set
        {
            if (SetProperty(ref _isRecommended, value))
            {
                OnPropertyChanged(nameof(DiscoveryTitle));
            }
        }
    }

    public string DiscoveryTitle => IsRecommended ? "热门推荐" : "搜索结果";

    public InstanceItem? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (SetProperty(ref _selectedInstance, value))
            {
                OnPropertyChanged(nameof(HasInstance));
                OnPropertyChanged(nameof(IsVanilla));
                OnPropertyChanged(nameof(InstanceSummary));
                _ = LoadModsAsync();
            }
        }
    }

    public bool HasInstance => SelectedInstance is not null;

    public bool IsVanilla => SelectedInstance?.Info.Manifest.Loader == LoaderKind.Vanilla;

    public string InstanceSummary => SelectedInstance is null
        ? "未选择实例"
        : $"{SelectedInstance.Info.Manifest.MinecraftVersion} · {SelectedInstance.LoaderLabel}";

    public bool HasInstalledMods => _allInstalledMods.Count > 0;

    public bool HasInstalledPaging => InstalledPageCount > 1;

    public int InstalledPageCount => Math.Max(
        1, (_allInstalledMods.Count + InstalledPageSize - 1) / InstalledPageSize);

    public int InstalledCurrentPage => _installedPage;

    public bool CanInstalledPrevious => _installedPage > 1;

    public bool CanInstalledNext => _installedPage < InstalledPageCount;

    public string InstalledPageText =>
        $"第 {_installedPage} / {InstalledPageCount} 页";

    public string InstalledTotalText => $"{_allInstalledMods.Count} 个文件";

    public bool HasResults => _allResults.Count > 0;

    public bool HasResultsPaging => ResultsPageCount > 1;

    public int ResultsPageCount => Math.Max(
        1, (_allResults.Count + ResultsPageSize - 1) / ResultsPageSize);

    public bool CanResultsPrevious => _resultsPage > 1;

    public bool CanResultsNext => _resultsPage < ResultsPageCount;

    public string ResultsPageText => $"第 {_resultsPage} / {ResultsPageCount} 页";

    public bool HasUpdates => Updates.Count > 0;

    public bool HasSearched
    {
        get => _hasSearched;
        private set => SetProperty(ref _hasSearched, value);
    }

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

    public ICommand SearchCommand { get; }

    public ICommand RecommendCommand { get; }

    public ICommand SelectCategoryCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand ToggleCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand CheckUpdatesCommand { get; }

    public ICommand ApplyUpdateCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand InstalledPreviousPageCommand { get; }

    public ICommand InstalledNextPageCommand { get; }

    public ICommand ResultsPreviousPageCommand { get; }

    public ICommand ResultsNextPageCommand { get; }

    public ICommand RefreshCommand { get; }

    private void SetInstalledPage(int page)
    {
        var target = Math.Clamp(page, 1, InstalledPageCount);
        if (target == _installedPage && InstalledMods.Count > 0)
        {
            return;
        }

        _installedPage = target;
        ApplyInstalledPage();
    }

    private void ApplyInstalledPage()
    {
        InstalledMods.Clear();
        foreach (var item in _allInstalledMods
                     .Skip((_installedPage - 1) * InstalledPageSize)
                     .Take(InstalledPageSize))
        {
            InstalledMods.Add(item);
        }

        OnPropertyChanged(nameof(InstalledCurrentPage));
        OnPropertyChanged(nameof(InstalledPageCount));
        OnPropertyChanged(nameof(InstalledPageText));
        OnPropertyChanged(nameof(InstalledTotalText));
        OnPropertyChanged(nameof(HasInstalledMods));
        OnPropertyChanged(nameof(HasInstalledPaging));
        OnPropertyChanged(nameof(CanInstalledPrevious));
        OnPropertyChanged(nameof(CanInstalledNext));
    }

    private void SetResultsPage(int page)
    {
        var target = Math.Clamp(page, 1, ResultsPageCount);
        if (target == _resultsPage && Results.Count > 0)
        {
            return;
        }

        _resultsPage = target;
        ApplyResultsPage();
    }

    private void ApplyResultsPage()
    {
        Results.Clear();
        foreach (var item in _allResults
                     .Skip((_resultsPage - 1) * ResultsPageSize)
                     .Take(ResultsPageSize))
        {
            Results.Add(item);
            _ = item.LoadIconAsync();
        }

        OnPropertyChanged(nameof(ResultsPageCount));
        OnPropertyChanged(nameof(ResultsPageText));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasResultsPaging));
        OnPropertyChanged(nameof(CanResultsPrevious));
        OnPropertyChanged(nameof(CanResultsNext));
    }

    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var preloadRecommendations = false;
        IsBusy = true;
        try
        {
            var instances = await _context.Instances.ListAsync().ConfigureAwait(true);
            var selectedId = SelectedInstance?.Info.Manifest.Id;
            Instances.Clear();
            foreach (var instance in instances.OrderByDescending(i => i.Manifest.LastPlayedAt))
            {
                Instances.Add(new InstanceItem(instance));
            }

            var preferred = Instances.FirstOrDefault(i => i.Info.Manifest.Id == selectedId)
                            ?? Instances.FirstOrDefault(i => i.Info.Manifest.Loader != LoaderKind.Vanilla)
                            ?? Instances.FirstOrDefault();

            var activeRoot = _settings.Current.Game.ActiveRoot;
            if (!string.IsNullOrWhiteSpace(activeRoot))
            {
                var linked = Instances.FirstOrDefault(item => string.Equals(
                    item.Info.Manifest.SourceRoot, activeRoot, StringComparison.OrdinalIgnoreCase));
                if (linked is null)
                {
                    try
                    {
                        var versions = await _context.GameRoots.ScanAsync(activeRoot)
                            .ConfigureAwait(true);
                        var latest = versions
                            .OrderByDescending(version => version.Loader != LoaderKind.Vanilla)
                            .ThenByDescending(version => version.ReleasedAt ?? DateTimeOffset.MinValue)
                            .FirstOrDefault();
                        if (latest is not null)
                        {
                            var external = new InstanceInfo
                            {
                                Directory = activeRoot,
                                Manifest = new InstanceManifest
                                {
                                    Id = "__external_root__",
                                    Name = $"{new DirectoryInfo(activeRoot).Name}（加载目录）",
                                    VersionId = latest.Id,
                                    MinecraftVersion = latest.MinecraftVersion,
                                    Loader = latest.Loader,
                                    SourceRoot = activeRoot,
                                    SourceVersionId = latest.Id
                                }
                            };
                            linked = new InstanceItem(external);
                            Instances.Insert(0, linked);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn($"读取游戏加载目录失败：{ex.Message}");
                    }
                }

                preferred = linked ?? preferred;
            }

            SelectedInstance = preferred;
            if (Instances.Count == 0)
            {
                Status = "还没有实例；仍可浏览热门模组，安装前再创建实例。";
            }

            preloadRecommendations = Results.Count == 0;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"读取实例失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        if (preloadRecommendations)
        {
            await RecommendAsync().ConfigureAwait(true);
        }
    }

    private async Task LoadModsAsync()
    {
        if (SelectedInstance is null)
        {
            return;
        }

        try
        {
            var mods = await _context.Mods.ScanAsync(SelectedInstance.Info).ConfigureAwait(true);
            _allInstalledMods.Clear();
            _allInstalledMods.AddRange(mods.Select(mod => new InstalledModItem(mod)));
            _installedPage = 1;
            ApplyInstalledPage();
            Status = IsVanilla
                ? "原版实例不能加载模组。请先安装 Fabric / Quilt / Forge / NeoForge。"
                : $"{mods.Count(m => m.IsEnabled)} 个启用 · {mods.Count(m => !m.IsEnabled)} 个禁用";
            StatusIsError = IsVanilla;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"读取模组失败：{ex.Message}";
        }
    }

    private async Task SearchFromQueryAsync()
    {
        IsRecommended = false;
        await SearchAsync().ConfigureAwait(true);
    }

    private async Task RecommendAsync()
    {
        IsRecommended = true;
        SearchText = string.Empty;
        await SearchAsync().ConfigureAwait(true);
    }

    private async Task SelectCategoryAsync(object? parameter)
    {
        if (parameter is ModFilterOption option)
        {
            SelectedCategory = option.Value;
            IsRecommended = true;
            SearchText = string.Empty;
            await SearchAsync().ConfigureAwait(true);
        }
    }

    private async Task SearchAsync()
    {
        if (IsBusy)
        {
            _pendingSearch = true;
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = "正在按关联词搜索 Modrinth…";
        try
        {
            var loader = string.IsNullOrWhiteSpace(SelectedLoader)
                ? SelectedInstance is null
                    ? null
                    : ModService.LoaderFacet(SelectedInstance.Info.Manifest.Loader)
                : SelectedLoader;
            var gameVersion = string.IsNullOrWhiteSpace(SelectedGameVersion)
                ? SelectedInstance?.Info.Manifest.MinecraftVersion
                : SelectedGameVersion;
            var terms = SearchTerms.Expand(SearchText);
            var perTerm = Math.Clamp(20 / terms.Count + 4, 6, 20);
            var searchTasks = terms.Select(term => _context.Modrinth.SearchAsync(
                    term,
                    "mod",
                    limit: perTerm,
                    gameVersion: gameVersion,
                    loader: loader,
                    category: string.IsNullOrWhiteSpace(SelectedCategory) ? null : SelectedCategory,
                    index: IsRecommended ? "downloads" : "relevance"))
                .ToList();

            var batches = await Task.WhenAll(searchTasks).ConfigureAwait(true);
            var hits = SearchTerms.Merge(
                batches.Select(batch => (IReadOnlyList<ModrinthSearchHit>)batch.Hits),
                hit => hit.ProjectId,
                24);

            _allResults.Clear();
            _allResults.AddRange(hits.Select(hit => new ModSearchItem(hit, _context.Http)));
            _resultsPage = 1;
            ApplyResultsPage();

            HasSearched = true;
            var total = batches.Sum(batch => batch.TotalHits);
            if (_allResults.Count == 0)
            {
                Status = $"没有找到与“{SearchText}”相关的模组；可以换一个关键词或放宽加载器筛选。";
            }
            else if (SelectedInstance is null)
            {
                Status = terms.Count > 1
                    ? $"已按 {terms.Count} 个关联词检索 · 共 {_allResults.Count} 个；选择实例后可安装。"
                    : $"找到 {total:N0} 个相关模组，显示 {_allResults.Count} 个；选择实例后可安装。";
            }
            else if (IsVanilla)
            {
                Status = $"已找到 {_allResults.Count} 个模组；原版实例需先安装加载器才能安装。";
                StatusIsError = true;
            }
            else
            {
                Status = terms.Count > 1
                    ? $"已按 {terms.Count} 个关联词检索 · 共 {_allResults.Count} 个。"
                    : $"找到 {total:N0} 个相关模组，显示 {_allResults.Count} 个。";
            }
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"搜索失败：{ex.Message}";
            AppLog.Error("Modrinth 模组搜索失败", ex);
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

    private async Task InstallAsync(object? parameter)
    {
        if (parameter is not ModSearchItem item || IsBusy)
        {
            return;
        }

        if (SelectedInstance is null)
        {
            StatusIsError = true;
            Status = "请先在版本管理中创建或选择一个实例，再安装模组。";
            return;
        }

        if (IsVanilla)
        {
            StatusIsError = true;
            Status = "原版实例没有加载器，请先安装 Fabric / Quilt / Forge / NeoForge。";
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Progress = 0;
        ProgressText = $"准备安装 {item.Title}…";
        Status = $"正在安装 {item.Title}";
        var task = TaskCenter.Current.Begin($"安装模组 {item.Title}", InstanceSummary);
        try
        {
            var progress = new Progress<DownloadProgress>(report =>
            {
                Progress = report.Percent;
                ProgressText = report.Item;
                task.Report(report);
            });
            await _context.Mods
                .InstallAsync(SelectedInstance.Info, item.Hit, progress)
                .ConfigureAwait(true);
            await LoadModsAsync().ConfigureAwait(true);
            Status = $"{item.Title} 已安装；重启游戏后生效。";
            Progress = 100;
            ProgressText = "安装完成";
            TaskCenter.Current.Complete(task, $"{item.Title} 已安装");
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

    private async Task ToggleAsync(object? parameter)
    {
        if (parameter is not InstalledModItem item || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _context.Mods.SetEnabledAsync(item.Mod, !item.Mod.IsEnabled).ConfigureAwait(true);
            await LoadModsAsync().ConfigureAwait(true);
            Status = $"{item.Name} 已{(item.Mod.IsEnabled ? "禁用" : "启用")}。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"切换失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteAsync(object? parameter)
    {
        if (parameter is not InstalledModItem item || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _context.Mods.DeleteAsync(item.Mod).ConfigureAwait(true);
            await LoadModsAsync().ConfigureAwait(true);
            Status = $"已删除 {item.Name}。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"删除失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportFilesAsync(IReadOnlyList<string> files)
    {
        if (SelectedInstance is null || IsBusy || files.Count == 0)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = $"正在导入 {files.Count} 个模组文件…";
        try
        {
            var imported = 0;
            foreach (var file in files.Where(f =>
                         f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
            {
                await _context.Mods.ImportAsync(SelectedInstance.Info, file).ConfigureAwait(true);
                imported++;
            }

            await LoadModsAsync().ConfigureAwait(true);
            Status = imported == 0
                ? "拖入的文件中没有 .jar 模组。"
                : $"已导入 {imported} 个模组文件。";
            StatusIsError = imported == 0;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"导入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportAsync()
    {
        if (SelectedInstance is null || IsBusy)
        {
            return;
        }

        var file = FileBrowserWindow.PickFile(
            "导入本地模组",
            "Minecraft 模组 (*.jar)|*.jar");
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _context.Mods.ImportAsync(SelectedInstance.Info, file).ConfigureAwait(true);
            await LoadModsAsync().ConfigureAwait(true);
            Status = $"已导入 {Path.GetFileName(file)}。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"导入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckUpdatesAsync()
    {
        if (SelectedInstance is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = "正在检查模组更新…";
        try
        {
            var updates = await _context.Mods.CheckUpdatesAsync(SelectedInstance.Info).ConfigureAwait(true);
            Updates.Clear();
            foreach (var update in updates)
            {
                Updates.Add(new ModUpdateItem(update));
            }

            OnPropertyChanged(nameof(HasUpdates));
            Status = updates.Count == 0
                ? "所有模组都是最新版本。"
                : $"{updates.Count} 个模组可以更新。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"检查更新失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyUpdateAsync(object? parameter)
    {
        if (parameter is not ModUpdateItem item || SelectedInstance is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        Progress = 0;
        ProgressText = $"正在更新 {item.Name}…";
        var task = TaskCenter.Current.Begin($"更新模组 {item.Name}", InstanceSummary);
        try
        {
            var progress = new Progress<DownloadProgress>(report =>
            {
                Progress = report.Percent;
                ProgressText = report.Item;
                task.Report(report);
            });
            await _context.Mods.UpdateAsync(SelectedInstance.Info, item.Update, progress)
                .ConfigureAwait(true);
            Updates.Remove(item);
            OnPropertyChanged(nameof(HasUpdates));
            await LoadModsAsync().ConfigureAwait(true);
            Status = $"{item.Name} 已更新。";
            TaskCenter.Current.Complete(task, $"{item.Name} 已更新");
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"更新失败：{ex.Message}";
            TaskCenter.Current.Fail(task, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenFolder()
    {
        if (SelectedInstance is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(SelectedInstance.Info.ModsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{SelectedInstance.Info.ModsDirectory}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening a folder is a convenience.
        }
    }
}
