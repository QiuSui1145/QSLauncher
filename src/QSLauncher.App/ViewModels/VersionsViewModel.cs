using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using QSLauncher.App.Services;
using QSLauncher.App.Views;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public sealed class VersionRow : ObservableObject
{
    public VersionRow(VersionSummary summary, bool installed)
    {
        Summary = summary;
        IsInstalled = installed;
    }

    public VersionSummary Summary { get; }

    public string Id => Summary.Id;

    public string Type => Summary.Type;

    public string TypeLabel => Summary.Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "旧测试版",
        "old_alpha" => "旧 Alpha",
        _ => Summary.Type
    };

    public string DateText => (Summary.ReleaseTime ?? Summary.Time)?.ToLocalTime()
        .ToString("yyyy-MM-dd") ?? "—";

    public bool IsInstalled { get; }

    public string InstallLabel => IsInstalled ? "已安装" : "可安装";
}

public sealed class GameRootOption
{
    public GameRootOption(string? path, string display, string detail, string? name = null)
    {
        Path = path;
        Display = display;
        Detail = detail;
        Name = name ?? display;
    }

    /// <summary>Null means the launcher-managed instance folder.</summary>
    public string? Path { get; }

    public string Display { get; }

    public string Detail { get; }

    public string Name { get; }

    public bool IsManaged => Path is null;

    public string Badge => IsManaged ? "隔离" : Name;

    public override string ToString() => Display;
}

public sealed record MountModeOption(GameRootMountMode Mode, string Label, string Detail)
{
    public override string ToString() => Label;
}

public sealed class ExternalVersionItem
{
    public ExternalVersionItem(ExternalVersionInfo info)
    {
        Info = info;
    }

    public ExternalVersionInfo Info { get; }

    public string Id => Info.Id;

    public string MinecraftVersion => Info.MinecraftVersion;

    public string LoaderLabel => Info.Loader switch
    {
        LoaderKind.Fabric => "Fabric",
        LoaderKind.Quilt => "Quilt",
        LoaderKind.Forge => "Forge",
        LoaderKind.NeoForge => "NeoForge",
        _ => "原版"
    };

    public string DateText => Info.ReleasedAt is { } time
        ? time.ToLocalTime().ToString("yyyy-MM-dd")
        : "未知日期";

    public string Detail => $"{LoaderLabel} · {DateText}";

    public string StatusText => Info.HasClientJar ? "可以直接启动" : "缺少客户端 JAR";

    public bool CanLaunch => Info.HasClientJar;
}

public sealed class InstanceItem : ObservableObject
{
    public InstanceItem(InstanceInfo info)
    {
        Info = info;
    }

    public InstanceInfo Info { get; }

    public string Name
    {
        get => Info.DisplayName;
        set
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? Info.Manifest.Id : value.Trim();
            if (Info.Manifest.Name == trimmed)
            {
                return;
            }

            Info.Manifest.Name = trimmed;
            OnPropertyChanged();
        }
    }

    public string VersionLabel => $"{Info.Manifest.MinecraftVersion} · {LoaderLabel}";

    public string LoaderLabel => Info.Manifest.Loader switch
    {
        LoaderKind.Vanilla => "原版",
        LoaderKind.Fabric => $"Fabric {Info.Manifest.LoaderVersion}",
        LoaderKind.Quilt => $"Quilt {Info.Manifest.LoaderVersion}",
        LoaderKind.Forge => $"Forge {Info.Manifest.LoaderVersion}",
        LoaderKind.NeoForge => $"NeoForge {Info.Manifest.LoaderVersion}",
        _ => "原版"
    };

    public string LastPlayedText => Info.Manifest.LastPlayedAt is { } time
        ? $"上次启动 {time.ToLocalTime():MM-dd HH:mm}"
        : "尚未启动";

    public string MemoryText => $"{Info.Manifest.MemoryMb} MB";

    public bool IsLinked => !string.IsNullOrWhiteSpace(Info.Manifest.SourceRoot);

    public string SourceLabel => IsLinked ? RootTag is { Length: > 0 } tag ? tag : "挂载目录" : "隔离实例";

    private string RootTag
    {
        get
        {
            var name = Info.Manifest.Name ?? string.Empty;
            var end = name.LastIndexOf(')');
            var start = end > 0 ? name.LastIndexOf('(', end - 1) : -1;
            return start >= 0 && end > start + 1 ? name[(start + 1)..end] : string.Empty;
        }
    }

    public string SourceRoot => Info.Manifest.SourceRoot ?? string.Empty;

    public int MemoryMb
    {
        get => Info.Manifest.MemoryMb;
        set
        {
            var clamped = Math.Clamp(value, 512, 32768);
            if (Info.Manifest.MemoryMb == clamped)
            {
                return;
            }

            Info.Manifest.MemoryMb = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MemoryText));
        }
    }

    public string JvmArgs
    {
        get => Info.Manifest.JvmArgs;
        set
        {
            if (Info.Manifest.JvmArgs == value)
            {
                return;
            }

            Info.Manifest.JvmArgs = value;
            OnPropertyChanged();
        }
    }

    public string GameArgs
    {
        get => Info.Manifest.GameArgs;
        set
        {
            if (Info.Manifest.GameArgs == value)
            {
                return;
            }

            Info.Manifest.GameArgs = value;
            OnPropertyChanged();
        }
    }

    public string JavaPath
    {
        get => Info.Manifest.JavaPath ?? string.Empty;
        set
        {
            if (Info.Manifest.JavaPath == value)
            {
                return;
            }

            Info.Manifest.JavaPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            Info.Manifest.JavaRuntimeId = null;
            OnPropertyChanged();
        }
    }
}

public sealed class VersionsViewModel : ObservableObject
{
    private readonly PortablePaths _paths;
    private readonly LauncherContext _context;
    private readonly SettingsService _settings;
    private readonly Func<Task<GameAccount>> _accountProvider;
    private readonly LogCenter _logs;
    private readonly List<VersionRow> _allVersions = [];
    private bool _loaded;
    private bool _isBusy;
    private bool _showSnapshots;
    private string _searchText = string.Empty;
    private string _status = "正在读取官方版本清单…";
    private bool _statusIsError;
    private double _progress;
    private string _progressText = string.Empty;
    private VersionRow? _selectedVersion;
    private InstanceItem? _selectedInstance;
    private GameRootOption? _selectedRoot;
    private bool _isScanningRoot;
    private MountModeOption _selectedMountMode;

    public VersionsViewModel(
        PortablePaths paths,
        LauncherContext context,
        SettingsService settings,
        Func<Task<GameAccount>> accountProvider,
        LogCenter logs)
    {
        _paths = paths;
        _context = context;
        _settings = settings;
        _accountProvider = accountProvider;
        _logs = logs;
        Versions = [];
        Instances = [];
        ExternalVersions = [];
        RootOptions = [];
        Roots = [];
        MountModes =
        [
            new(GameRootMountMode.Single, "单目录挂载", "同一时间只加载一个 .minecraft 目录"),
            new(GameRootMountMode.Multiple, "多目录挂载", "同时加载所有目录，版本用目录名区分")
        ];
        _selectedMountMode = MountModes.First(option => option.Mode == settings.Current.Game.MountMode);
        AddRootCommand = new AsyncRelayCommand(AddRootAsync);
        RenameRootCommand = new AsyncRelayCommand(RenameRootAsync);
        RemoveRootCommand = new AsyncRelayCommand(RemoveRootAsync);
        PlayExternalCommand = new AsyncRelayCommand(PlayExternalAsync);
        ScanRootCommand = new AsyncRelayCommand(() => LoadExternalAsync(force: true));
        BuildRootOptions();

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        InstallCommand = new AsyncRelayCommand(InstallSelectedAsync);
        SaveInstanceCommand = new AsyncRelayCommand(SaveInstanceAsync);
        EditInstanceCommand = new AsyncRelayCommand(EditInstanceAsync);
        OpenLoaderCommand = new RelayCommand(OpenLoader);
        PlayInstanceCommand = new AsyncRelayCommand(PlayInstanceAsync);
        DeleteInstanceCommand = new AsyncRelayCommand(DeleteInstanceAsync);
        CopyInstanceCommand = new AsyncRelayCommand(CopyInstanceAsync);
        OpenInstanceFolderCommand = new RelayCommand(parameter =>
        {
            if (parameter is InstanceItem item)
            {
                OpenFolder(item.Info.Manifest.SourceRoot ?? item.Info.Directory);
            }
        });
        SelectVersionCommand = new RelayCommand(parameter =>
        {
            if (parameter is VersionRow row)
            {
                SelectedVersion = row;
            }
        });

        _ = LoadAsync();
    }

    public Task RefreshAsync() => LoadAsync();

    public ObservableCollection<VersionRow> Versions { get; }

    public ObservableCollection<InstanceItem> Instances { get; }

    public ObservableCollection<GameRootOption> RootOptions { get; }

    public ObservableCollection<GameRootOption> Roots { get; }

    public IReadOnlyList<MountModeOption> MountModes { get; }

    public ObservableCollection<ExternalVersionItem> ExternalVersions { get; }

    public MountModeOption SelectedMountMode
    {
        get => _selectedMountMode;
        set
        {
            if (value is null || !SetProperty(ref _selectedMountMode, value))
            {
                return;
            }

            _settings.Current.Game.MountMode = value.Mode;
            _ = SaveSettingsAsync();
            OnPropertyChanged(nameof(IsSingleMount));
            OnPropertyChanged(nameof(IsMultipleMount));
            OnPropertyChanged(nameof(RootCountText));
            OnPropertyChanged(nameof(IsManagedRoot));
            OnPropertyChanged(nameof(IsExternalRoot));
            OnPropertyChanged(nameof(ExternalRootLabel));
            _ = LoadExternalAsync(force: true);
        }
    }

    public bool IsSingleMount => SelectedMountMode.Mode == GameRootMountMode.Single;

    public bool IsMultipleMount => SelectedMountMode.Mode == GameRootMountMode.Multiple;

    public bool HasRoots => Roots.Count > 0;

    public string RootCountText => Roots.Count == 0
        ? "尚未添加游戏目录"
        : $"{Roots.Count} 个目录 · 版本名后显示目录标签";

    public GameRootOption? SelectedRoot
    {
        get => _selectedRoot;
        set
        {
            if (!SetProperty(ref _selectedRoot, value) || value is null)
            {
                return;
            }

            _settings.Current.Game.ActiveRoot = value.Path;
            _ = SaveSettingsAsync();
            OnPropertyChanged(nameof(IsManagedRoot));
            OnPropertyChanged(nameof(IsExternalRoot));
            OnPropertyChanged(nameof(ExternalRootLabel));
            _ = LoadExternalAsync(force: true);
        }
    }

    public bool IsManagedRoot => IsSingleMount && (SelectedRoot?.IsManaged ?? true);

    public bool IsExternalRoot => IsSingleMount && !IsManagedRoot;

    public string ExternalRootLabel => IsSingleMount
        ? SelectedRoot is { IsManaged: false } root
            ? root.Path ?? string.Empty
            : "由启动器管理的隔离实例"
        : Roots.Count == 0
            ? "尚未添加游戏目录"
            : $"同时挂载 {Roots.Count} 个目录";

    public bool IsScanningRoot
    {
        get => _isScanningRoot;
        private set => SetProperty(ref _isScanningRoot, value);
    }

    public bool HasExternalVersions => ExternalVersions.Count > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    public bool CanInteract => !IsBusy;

    public bool ShowSnapshots
    {
        get => _showSnapshots;
        set
        {
            if (SetProperty(ref _showSnapshots, value))
            {
                RebuildVersionList();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RebuildVersionList();
            }
        }
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

    public bool HasInstances => Instances.Count > 0;

    public VersionRow? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
            {
                OnPropertyChanged(nameof(HasSelectedVersion));
                OnPropertyChanged(nameof(SelectedVersionTitle));
                OnPropertyChanged(nameof(SelectedVersionMeta));
                OnPropertyChanged(nameof(SelectedVersionHint));
                OnPropertyChanged(nameof(InstallActionLabel));
            }
        }
    }

    public bool HasSelectedVersion => SelectedVersion is not null;

    public string SelectedVersionTitle => SelectedVersion?.Id ?? "选择一个版本";

    public string SelectedVersionMeta => SelectedVersion is null
        ? "正式版与快照均来自 Mojang 官方清单。"
        : $"{SelectedVersion.TypeLabel} · {SelectedVersion.DateText} · 官方源";

    public string SelectedVersionHint => SelectedVersion is null
        ? "挑选版本后即可创建独立实例，资源文件会自动校验。"
        : SelectedVersion.IsInstalled
            ? "该版本已有实例，可以从右侧直接启动或再创建一个独立实例。"
            : "将创建独立游戏目录，并下载客户端、依赖库与资源文件。";

    public string InstallActionLabel => SelectedVersion?.IsInstalled == true
        ? "再创建一个实例"
        : "安装并创建实例";

    public InstanceItem? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (SetProperty(ref _selectedInstance, value))
            {
                OnPropertyChanged(nameof(HasSelectedInstance));
            }
        }
    }

    public bool HasSelectedInstance => SelectedInstance is not null;

    public ICommand RefreshCommand { get; }

    public ICommand AddRootCommand { get; }

    public ICommand RenameRootCommand { get; }

    public ICommand RemoveRootCommand { get; }

    public ICommand PlayExternalCommand { get; }

    public ICommand ScanRootCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand PlayInstanceCommand { get; }

    public ICommand DeleteInstanceCommand { get; }

    public ICommand CopyInstanceCommand { get; }

    public ICommand OpenInstanceFolderCommand { get; }

    public ICommand SaveInstanceCommand { get; }

    public ICommand EditInstanceCommand { get; }

    public ICommand OpenLoaderCommand { get; }

    public ICommand SelectVersionCommand { get; }

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = _loaded ? "正在刷新官方版本清单…" : "正在读取本地实例…";
        try
        {
            // Local instances are the part that must work offline. Load them
            // before touching the network so a blocked/slow Mojang manifest
            // never hides the user's own library.
            var instances = await _context.Instances.ListAsync().ConfigureAwait(true);
            var activePaths = ActiveRootOptions()
                .Select(root => root.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Instances.Clear();
            foreach (var instance in instances.Where(instance =>
                         string.IsNullOrWhiteSpace(instance.Manifest.SourceRoot)
                         || activePaths.Contains(instance.Manifest.SourceRoot)))
            {
                Instances.Add(new InstanceItem(instance));
            }

            OnPropertyChanged(nameof(HasInstances));
            SelectedInstance ??= Instances.FirstOrDefault();

            try
            {
                var manifest = await _context.Mojang.GetManifestAsync(refresh: _loaded)
                    .ConfigureAwait(true);
                var installedVersions = instances
                    .Select(i => i.Manifest.MinecraftVersion)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                _allVersions.Clear();
                foreach (var version in manifest.Versions)
                {
                    _allVersions.Add(new VersionRow(version, installedVersions.Contains(version.Id)));
                }

                RebuildVersionList();
                SelectedVersion ??= Versions.FirstOrDefault();
                _loaded = true;
                Status = $"已同步 {manifest.Versions.Count} 个官方版本 · 本地 {instances.Count} 个实例";
                AppLog.Info($"版本清单就绪：{manifest.Latest.Release}，实例 {instances.Count} 个。");
                await LoadExternalAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusIsError = true;
                Status = $"官方版本清单暂时不可用，本地 {Instances.Count} 个实例仍可使用：{ex.Message}";
                AppLog.Error("版本清单加载失败", ex);
            }
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"读取实例失败：{ex.Message}";
            AppLog.Error("实例列表加载失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildRootOptions()
    {
        RootOptions.Clear();
        Roots.Clear();
        RootOptions.Add(new GameRootOption(null, "本启动器实例", "仅使用强制隔离实例"));
        foreach (var root in _settings.Current.Game.Roots)
        {
            if (string.IsNullOrWhiteSpace(root.Path) || !Directory.Exists(root.Path))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(root.Name)
                ? new DirectoryInfo(root.Path).Name
                : root.Name;
            var option = new GameRootOption(root.Path, name, root.Path, name);
            RootOptions.Add(option);
            Roots.Add(option);
        }

        var active = _settings.Current.Game.ActiveRoot;
        _selectedRoot = RootOptions.FirstOrDefault(option =>
                          string.Equals(option.Path, active, StringComparison.OrdinalIgnoreCase))
                      ?? RootOptions[0];
        OnPropertyChanged(nameof(SelectedRoot));
        OnPropertyChanged(nameof(IsManagedRoot));
        OnPropertyChanged(nameof(IsExternalRoot));
        OnPropertyChanged(nameof(ExternalRootLabel));
        OnPropertyChanged(nameof(RootCountText));
        OnPropertyChanged(nameof(HasRoots));
    }

    private async Task AddRootAsync()
    {
        var folder = FileBrowserWindow.PickFolder(
            "添加游戏加载目录（选择 .minecraft 或其上一级）",
            SelectedRoot?.Path);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        try
        {
            var normalized = _context.GameRoots.Normalize(folder);
            var existing = _settings.Current.Game.Roots.FirstOrDefault(root =>
                string.Equals(root.Path, normalized, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _settings.Current.Game.ActiveRoot = normalized;
                if (IsSingleMount)
                {
                    await _settings.SaveAsync().ConfigureAwait(true);
                }

                BuildRootOptions();
                await LoadExternalAsync(force: true).ConfigureAwait(true);
                Status = $"该目录已经添加：{existing.Name}";
                StatusIsError = false;
                return;
            }

            var baseName = new DirectoryInfo(normalized).Name;
            var initial = UniqueRootName(baseName);
            var name = TextPromptWindow.Show(
                "为游戏目录命名",
                "每个 .minecraft 目录必须有独立名称。版本列表会显示为“版本名(目录名)”，目录名不能重复。",
                initial,
                value => ValidateRootName(value));
            if (name is null)
            {
                return;
            }

            _settings.Current.Game.Roots.Add(new GameRootSetting
            {
                Path = normalized,
                Name = name.Trim()
            });
            _settings.Current.Game.ActiveRoot = normalized;
            await _settings.SaveAsync().ConfigureAwait(true);

            BuildRootOptions();
            await LoadExternalAsync(force: true).ConfigureAwait(true);
            Status = $"已添加游戏目录“{name.Trim()}”，版本已进入实例总库。";
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"目录不可用：{ex.Message}";
        }
    }

    private async Task RenameRootAsync(object? parameter)
    {
        if (parameter is not GameRootOption option || option.IsManaged || option.Path is null)
        {
            return;
        }

        var setting = _settings.Current.Game.Roots.FirstOrDefault(root =>
            string.Equals(root.Path, option.Path, StringComparison.OrdinalIgnoreCase));
        if (setting is null)
        {
            return;
        }

        var name = TextPromptWindow.Show(
            "重命名游戏目录",
            "新的名称会作为该目录中所有版本的标签，并立即更新实例总库；目录本身不会移动。",
            setting.Name ?? option.Name,
            value => ValidateRootName(value, option.Path));
        if (name is null || string.Equals(name.Trim(), setting.Name, StringComparison.Ordinal))
        {
            return;
        }

        setting.Name = name.Trim();
        await _settings.SaveAsync().ConfigureAwait(true);

        var linked = await _context.Instances.ListAsync().ConfigureAwait(true);
        foreach (var instance in linked.Where(instance =>
                     string.Equals(instance.Manifest.SourceRoot, option.Path,
                         StringComparison.OrdinalIgnoreCase)))
        {
            instance.Manifest.Name = TaggedName(instance.Manifest.SourceVersionId
                                               ?? instance.Manifest.VersionId, setting.Name);
            await _context.Instances.SaveAsync(instance).ConfigureAwait(true);
        }

        BuildRootOptions();
        await LoadExternalAsync(force: true).ConfigureAwait(true);
        Status = $"游戏目录已重命名为“{setting.Name}”。";
        StatusIsError = false;
    }

    private async Task RemoveRootAsync(object? parameter)
    {
        if (parameter is not GameRootOption option || option.IsManaged || option.Path is null)
        {
            return;
        }

        if (!AppDialog.Confirm(
                "移除游戏目录",
                $"从挂载列表移除“{option.Name}”？\n\n不会删除 .minecraft 中的任何文件；该目录的版本会从实例总库隐藏。",
                "移除目录",
                danger: true))
        {
            return;
        }

        var roots = _settings.Current.Game.Roots;
        roots.RemoveAll(root => string.Equals(root.Path, option.Path, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(_settings.Current.Game.ActiveRoot, option.Path, StringComparison.OrdinalIgnoreCase))
        {
            _settings.Current.Game.ActiveRoot = roots.FirstOrDefault()?.Path;
        }

        await _settings.SaveAsync().ConfigureAwait(true);

        // Remove only the lightweight instance records that point at this root.
        // The .minecraft directory and every game file inside it stay untouched.
        var linked = (await _context.Instances.ListAsync().ConfigureAwait(true))
            .Where(instance => string.Equals(instance.Manifest.SourceRoot, option.Path,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var instance in linked)
        {
            try
            {
                await _context.Instances.QuarantineAsync(instance).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error($"移除挂载目录 {option.Path} 的实例记录失败", ex);
            }
        }

        BuildRootOptions();
        await LoadExternalAsync(force: true).ConfigureAwait(true);
        Status = $"已移除目录“{option.Name}”，目录中的游戏文件保持不变。";
        StatusIsError = false;
    }

    private string? ValidateRootName(string value, string? exceptPath = null)
    {
        var name = value.Trim();
        if (name.Length == 0)
        {
            return "名称不能为空。";
        }

        if (_settings.Current.Game.Roots.Any(root =>
                !string.Equals(root.Path, exceptPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals((root.Name ?? string.Empty).Trim(), name,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return "该目录名称已被使用，请换一个名称。";
        }

        return null;
    }

    private string UniqueRootName(string baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "游戏目录" : baseName.Trim();
        if (ValidateRootName(name) is null)
        {
            return name;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{name}-{index}";
            if (ValidateRootName(candidate) is null)
            {
                return candidate;
            }
        }
    }

    private static string TaggedName(string versionId, string rootName) =>
        $"{versionId}({rootName})";

    private IReadOnlyList<GameRootOption> ActiveRootOptions() =>
        IsMultipleMount
            ? Roots.ToList()
            : SelectedRoot is { IsManaged: false } selected
                ? [selected]
                : [];

    public async Task LoadExternalAsync(bool force = false)
    {
        var activeRoots = ActiveRootOptions();
        if (activeRoots.Count == 0)
        {
            ExternalVersions.Clear();
            OnPropertyChanged(nameof(HasExternalVersions));
            await ReloadInstancesAsync().ConfigureAwait(true);
            return;
        }

        if (IsScanningRoot)
        {
            return;
        }

        IsScanningRoot = true;
        try
        {
            var existing = (await _context.Instances.ListAsync().ConfigureAwait(true)).ToList();
            var foundTotal = 0;
            ExternalVersions.Clear();

            foreach (var root in activeRoots)
            {
                if (string.IsNullOrWhiteSpace(root.Path))
                {
                    continue;
                }

                var found = await _context.GameRoots.ScanAsync(root.Path).ConfigureAwait(true);
                foundTotal += found.Count;
                foreach (var version in found)
                {
                    ExternalVersions.Add(new ExternalVersionItem(version));
                    var tag = TaggedName(version.Id, root.Name);
                    var linked = existing.FirstOrDefault(instance =>
                        string.Equals(instance.Manifest.SourceRoot, root.Path,
                            StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            instance.Manifest.SourceVersionId ?? instance.Manifest.VersionId,
                            version.Id,
                            StringComparison.OrdinalIgnoreCase));

                    if (linked is null)
                    {
                        linked = await _context.Instances.CreateLinkedAsync(tag, version)
                            .ConfigureAwait(true);
                        existing.Add(linked);
                    }
                    else
                    {
                        var changed = false;
                        if (!string.Equals(linked.Manifest.Name, tag, StringComparison.Ordinal))
                        {
                            linked.Manifest.Name = tag;
                            changed = true;
                        }

                        if (!string.Equals(linked.Manifest.MinecraftVersion, version.MinecraftVersion,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            linked.Manifest.MinecraftVersion = version.MinecraftVersion;
                            linked.Manifest.Loader = version.Loader;
                            changed = true;
                        }

                        if (changed)
                        {
                            await _context.Instances.SaveAsync(linked).ConfigureAwait(true);
                        }
                    }
                }
            }

            OnPropertyChanged(nameof(HasExternalVersions));
            await ReloadInstancesAsync().ConfigureAwait(true);
            Status = IsMultipleMount
                ? $"已同时挂载 {activeRoots.Count} 个目录 · 共 {foundTotal} 个版本"
                : $"已载入游戏加载目录：{activeRoots[0].Name} · {foundTotal} 个版本";
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            ExternalVersions.Clear();
            OnPropertyChanged(nameof(HasExternalVersions));
            StatusIsError = true;
            Status = $"载入游戏加载目录失败：{ex.Message}";
            AppLog.Error("扫描 .minecraft 目录失败", ex);
        }
        finally
        {
            IsScanningRoot = false;
        }
    }

    private async Task PlayExternalAsync(object? parameter)
    {
        if (parameter is not ExternalVersionItem version || IsBusy)
        {
            return;
        }

        InstanceItem target;
        IsBusy = true;
        StatusIsError = false;
        Status = $"正在从当前游戏目录启动 {version.Id}…";
        try
        {
            var existing = Instances.FirstOrDefault(item =>
                string.Equals(item.Info.Manifest.SourceRoot, version.Info.RootPath,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    item.Info.Manifest.SourceVersionId ?? item.Info.Manifest.VersionId,
                    version.Id,
                    StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                target = existing;
            }
            else
            {
                var rootName = Roots.FirstOrDefault(root =>
                                   string.Equals(root.Path, version.Info.RootPath,
                                       StringComparison.OrdinalIgnoreCase))?.Name
                               ?? new DirectoryInfo(version.Info.RootPath).Name;
                var instance = await _context.Instances.CreateLinkedAsync(
                    TaggedName(version.Id, rootName), version.Info).ConfigureAwait(true);
                target = new InstanceItem(instance);
                Instances.Insert(0, target);
                OnPropertyChanged(nameof(HasInstances));
            }

            SelectedInstance = target;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"启动准备失败：{ex.Message}";
            AppLog.Error($"准备挂载版本 {version.Id} 失败", ex);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await PlayInstanceAsync(target).ConfigureAwait(true);
    }

    private Task SaveSettingsAsync() => _settings.SaveAsync();

    private void RebuildVersionList()
    {
        var query = SearchText.Trim();
        var rows = _allVersions
            .Where(v => ShowSnapshots || v.Type == "release")
            .Where(v => query.Length == 0 || v.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(400)
            .ToList();

        Versions.Clear();
        foreach (var row in rows)
        {
            Versions.Add(row);
        }

        if (SelectedVersion is not null && !rows.Contains(SelectedVersion))
        {
            SelectedVersion = rows.FirstOrDefault();
        }
    }

    private async Task InstallSelectedAsync()
    {
        var row = SelectedVersion;
        if (row is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Progress = 0;
        ProgressText = "准备下载…";
        Status = $"正在安装 {row.Id}";
        var task = TaskCenter.Current.Begin($"安装 Minecraft {row.Id}", "准备版本清单…");
        try
        {
            var instance = await _context.Instances.CreateAsync(
                row.Id,
                row.Id,
                row.Id,
                LoaderKind.Vanilla,
                cancellationToken: CancellationToken.None).ConfigureAwait(true);

            var progress = new Progress<DownloadProgress>(report =>
            {
                Progress = report.Percent;
                ProgressText = report.Item;
                task.Report(report);
            });

            await _context.Installer.InstallAsync(
                instance,
                progress,
                CancellationToken.None).ConfigureAwait(true);

            await ReloadInstancesAsync().ConfigureAwait(true);
            SelectedInstance = Instances.FirstOrDefault(i => i.Info.Manifest.Id == instance.Manifest.Id)
                              ?? Instances.FirstOrDefault();
            Status = $"{row.Id} 安装完成，可以启动。";
            Progress = 100;
            ProgressText = "安装完成";
            TaskCenter.Current.Complete(task, $"{row.Id} 已安装到版本管理");
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"安装失败：{ex.Message}";
            ProgressText = "安装失败";
            TaskCenter.Current.Fail(task, ex.Message);
            AppLog.Error($"安装 {row.Id} 失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadInstancesAsync()
    {
        var instances = await _context.Instances.ListAsync().ConfigureAwait(true);
        var activePaths = ActiveRootOptions()
            .Select(root => root.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedId = SelectedInstance?.Info.Manifest.Id;
        Instances.Clear();
        foreach (var instance in instances.Where(instance =>
                     string.IsNullOrWhiteSpace(instance.Manifest.SourceRoot)
                     || activePaths.Contains(instance.Manifest.SourceRoot)))
        {
            Instances.Add(new InstanceItem(instance));
        }

        OnPropertyChanged(nameof(HasInstances));
        SelectedInstance = (selectedId is null
            ? null
            : Instances.FirstOrDefault(item => item.Info.Manifest.Id == selectedId))
            ?? Instances.FirstOrDefault();
    }

    private async Task PlayInstanceAsync(object? parameter)
    {
        if (parameter is not InstanceItem item || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = $"正在准备启动 {item.Name}…";
        _logs.BeginLaunch(item.Name);
        _logs.SetStage("login", LaunchStageState.Running, "正在读取当前账号");
        var progressWindow = new LaunchProgressWindow(_logs);
        if (Application.Current?.MainWindow is { IsLoaded: true } owner)
        {
            progressWindow.Owner = owner;
        }

        progressWindow.Show();

        try
        {
            var account = await _accountProvider().ConfigureAwait(true);
            _logs.SetStage("login", LaunchStageState.Done,
                $"{account.Name} · {account.Kind}");
            _logs.SetStage("verify", LaunchStageState.Running, "正在解析版本与 Java");
            var version = string.IsNullOrWhiteSpace(item.Info.Manifest.SourceRoot)
                ? await _context.Mojang.ResolveAsync(item.Info.Manifest.VersionId).ConfigureAwait(true)
                : await _context.Mojang.ResolveFromRootAsync(
                        item.Info.Manifest.SourceRoot!,
                        item.Info.Manifest.SourceVersionId ?? item.Info.Manifest.VersionId)
                    .ConfigureAwait(true);
            var required = version.JavaVersion?.MajorVersion ?? 8;
            var java = await ResolveJavaAsync(required, item.Info).ConfigureAwait(true);
            if (java is null)
            {
                throw new InvalidOperationException(
                    $"该版本需要 Java {required}，但没有找到可用的 Java。请先在 Java 页面安装运行时。");
            }

            _logs.SetStage("verify", LaunchStageState.Done,
                $"{version.Id} · Java {java.MajorVersion}");
            _logs.SetStage("fill", LaunchStageState.Running, "正在检查并补全客户端文件");
            var installProgress = new Progress<DownloadProgress>(report =>
            {
                _logs.SetProgress($"{report.Percent:0}% · {report.Item}");
            });

            var session = await _context.Launcher.LaunchAsync(
                item.Info,
                account,
                java,
                new LaunchOptions
                {
                    MemoryMb = item.Info.Manifest.MemoryMb,
                    JvmArgs = item.Info.Manifest.JvmArgs,
                    GameArgs = item.Info.Manifest.GameArgs
                },
                installProgress: installProgress,
                output: line => _logs.Append(line)).ConfigureAwait(true);

            _logs.AttachLogFile(session.LogPath);
            _logs.SetStage("fill", LaunchStageState.Done, "资源文件已就绪");
            _logs.SetStage("starting", LaunchStageState.Running, "游戏进程已创建，等待主窗口");
            await Task.Delay(900).ConfigureAwait(true);
            _logs.SetStage("starting", LaunchStageState.Done, "游戏已开始运行");
            _logs.CompleteLaunch();

            Status = $"游戏已启动 · Java {java.MajorVersion} · 日志 {Path.GetFileName(session.LogPath)}";
            _ = session.WaitForExitAsync().ContinueWith(
                _ =>
                {
                    var exitCode = 0;
                    try
                    {
                        exitCode = session.Process.ExitCode;
                    }
                    catch
                    {
                        // Process may already be disposed.
                    }

                    _logs.MarkExited(exitCode);
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        Status = $"游戏已退出 · {item.Name}";
                    });
                },
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            _logs.Fail(ex.Message);
            StatusIsError = true;
            Status = $"启动失败：{ex.Message}";
            AppLog.Error($"启动实例 {item.Name} 失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<JavaRuntimeInfo?> ResolveJavaAsync(int required, InstanceInfo instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.Manifest.JavaPath)
            && File.Exists(instance.Manifest.JavaPath))
        {
            var instanceJava = await JavaService.ProbeAsync(instance.Manifest.JavaPath, true)
                .ConfigureAwait(true);
            if (instanceJava is not null && instanceJava.MajorVersion >= required)
            {
                return instanceJava;
            }
        }

        // Prefer an exact major match first. Old versions often break on a
        // newer Java, while modern versions require 21/25, so a single global
        // runtime must never override the version's own requirement.
        var automatic = await _context.Java.FindBestAsync(required).ConfigureAwait(true);
        if (automatic is not null && automatic.MajorVersion == required)
        {
            return automatic;
        }

        var custom = _settings.Current.Java.CustomJavaPath;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
        {
            var probed = await JavaService.ProbeAsync(custom, false).ConfigureAwait(true);
            if (probed is not null && probed.MajorVersion >= required)
            {
                return probed;
            }
        }

        return automatic ?? await _context.Java
            .FindBestAsync(required, _settings.Current.Java.DefaultRuntimeId)
            .ConfigureAwait(true);
    }

    private void OpenLoader(object? parameter)
    {
        if (SelectedVersion is null)
        {
            return;
        }

        var window = new LoaderWindow(
            _context,
            SelectedVersion.Id,
            null,
            _ => Status = $"{SelectedVersion.Id} 的加载器已安装，正在刷新实例列表。");
        if (Application.Current?.MainWindow is { IsLoaded: true } owner)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        if (window.DialogResult == true)
        {
            _ = LoadAsync();
        }
    }

    private async Task EditInstanceAsync(object? parameter)
    {
        if (parameter is not InstanceItem item)
        {
            return;
        }

        var window = new InstanceSettingsWindow(item);
        if (Application.Current?.MainWindow is { IsLoaded: true } owner)
        {
            window.Owner = owner;
        }

        if (window.ShowDialog() == true)
        {
            await SaveInstanceAsync(item).ConfigureAwait(true);
        }
    }

    public async Task InstallModpackOverlayAsync(string archivePath)
    {
        if (IsBusy)
        {
            return;
        }

        var item = SelectedInstance;
        if (item is null)
        {
            var instances = await _context.Instances.ListAsync().ConfigureAwait(true);
            if (instances.Count == 0)
            {
                AppDialog.Alert("没有可用实例", "请先创建一个实例，再把整合包拖入启动器。");
                return;
            }

            await ReloadInstancesAsync().ConfigureAwait(true);
            item = SelectedInstance;
        }

        if (item is null)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Progress = 0;
        ProgressText = "读取整合包…";
        Status = $"正在把整合包安装到「{item.Name}」";
        var task = TaskCenter.Current.Begin(
            $"安装整合包到 {item.Name}",
            Path.GetFileName(archivePath));
        try
        {
            var progress = new Progress<DownloadProgress>(report =>
            {
                Progress = report.Percent;
                ProgressText = report.Item;
                task.Report(report);
            });
            await _context.Modpacks.InstallOverlayAsync(
                    archivePath, item.Info, progress)
                .ConfigureAwait(true);
            await ReloadInstancesAsync().ConfigureAwait(true);
            Status = $"{item.Name} 已更新：整合包内容已覆盖到当前挂载目录。";
            Progress = 100;
            ProgressText = "整合包安装完成";
            TaskCenter.Current.Complete(task, "整合包已安装到当前实例");
            AppDialog.Alert(
                "整合包安装完成",
                $"{Path.GetFileName(archivePath)} 已安装到当前实例「{item.Name}」。\n\n" +
                "如果整合包清单中的模组没有下载地址，启动器会保留本地已存在的文件。");
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"整合包安装失败：{ex.Message}";
            ProgressText = "安装失败";
            TaskCenter.Current.Fail(task, ex.Message);
            AppLog.Error("整合包拖放安装失败", ex);
            AppDialog.Alert("整合包安装失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveInstanceAsync(object? parameter)
    {
        var item = parameter as InstanceItem ?? SelectedInstance;
        if (item is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        try
        {
            await _context.Instances.SaveAsync(item.Info).ConfigureAwait(true);
            Status = $"{item.Name} 的设置已保存。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CopyInstanceAsync(object? parameter)
    {
        if (parameter is not InstanceItem item || IsBusy)
        {
            return;
        }

        if (!AppDialog.Confirm(
                "复制实例",
                $"复制实例“{item.Name}”？\n\n会复制模组、资源包、光影、配置与存档；natives 与日志会在下次启动时重新生成。",
                "开始复制"))
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = "正在复制实例…";
        try
        {
            var copy = await _context.Instances.CopyAsync(item.Info).ConfigureAwait(true);
            await ReloadInstancesAsync().ConfigureAwait(true);
            SelectedInstance = Instances.FirstOrDefault(i => i.Info.Manifest.Id == copy.Manifest.Id)
                               ?? Instances.FirstOrDefault();
            Status = $"已复制为“{copy.DisplayName}”。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"复制失败：{ex.Message}";
            AppLog.Error("复制实例失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteInstanceAsync(object? parameter)
    {
        if (parameter is not InstanceItem item || IsBusy)
        {
            return;
        }

        if (!AppDialog.Confirm(
                "删除实例",
                $"确定要删除实例“{item.Name}”吗？\n\n实例目录会移动到 data\\quarantine，便于恢复。",
                "移入隔离区",
                danger: true))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _context.Instances.QuarantineAsync(item.Info).ConfigureAwait(true);
            await ReloadInstancesAsync().ConfigureAwait(true);
            SelectedInstance = Instances.FirstOrDefault();
            Status = $"已移入隔离区：{item.Name}";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"删除失败：{ex.Message}";
            AppLog.Error("删除实例失败", ex);
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
