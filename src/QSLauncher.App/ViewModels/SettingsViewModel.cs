using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using QSLauncher.App.Services;
using QSLauncher.App.Views;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public sealed class HomeWidgetToggle : ObservableObject
{
    private readonly HomeWidgetSetting _setting;
    private readonly Action _changed;

    public HomeWidgetToggle(string title, HomeWidgetSetting setting, Action changed)
    {
        Title = title;
        _setting = setting;
        _changed = changed;
    }

    public string Title { get; }

    public bool IsVisible
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
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly BackgroundService _background;
    private readonly PortablePaths _paths;
    private string _accentInput;
    private string _statusMessage = string.Empty;
    private bool _statusIsError;
    private bool _suppress;
    private CancellationTokenSource? _saveCts;

    public SettingsViewModel(
        PortablePaths paths,
        SettingsService settings,
        ThemeService theme,
        BackgroundService background)
    {
        _paths = paths;
        _settings = settings;
        _theme = theme;
        _background = background;
        _accentInput = settings.Current.Appearance.AccentColor;

        AccentPresets =
        [
            new("翡翠", "#4ADE80"),
            new("青瓷", "#2DD4BF"),
            new("海盐", "#38BDF8"),
            new("极光", "#22D3EE"),
            new("鸢尾", "#A78BFA"),
            new("莓红", "#FB7185"),
            new("霞光", "#F472B6"),
            new("暮橙", "#FB923C"),
            new("沙金", "#EAB308"),
            new("石墨", "#94A3B8")
        ];

        HomeWidgets =
        [
            new("启动实例", HomeLayoutDefaults.Get(settings.Current.Home, HomeWidgetKeys.Launch), ScheduleSave),
            new("快捷操作", HomeLayoutDefaults.Get(settings.Current.Home, HomeWidgetKeys.Quick), ScheduleSave),
            new("实例统计", HomeLayoutDefaults.Get(settings.Current.Home, HomeWidgetKeys.Stats), ScheduleSave),
            new("最近实例", HomeLayoutDefaults.Get(settings.Current.Home, HomeWidgetKeys.Recent), ScheduleSave)
        ];

        SelectAccentCommand = new RelayCommand(parameter =>
        {
            if (parameter is AccentPreset preset)
            {
                ApplyAccent(preset.Hex);
            }
        });
        ApplyAccentCommand = new RelayCommand(() => ApplyAccent(AccentInput));
        SelectImageCommand = new RelayCommand(() => SelectBackground(BackgroundMode.Image));
        SelectVideoCommand = new RelayCommand(() => SelectBackground(BackgroundMode.Video));
        ClearBackgroundCommand = new RelayCommand(ClearBackground);
        SelectLogoCommand = new RelayCommand(SelectLogo);
        ClearLogoCommand = new RelayCommand(ClearLogo);
        OpenBackgroundsFolderCommand = new RelayCommand(() => OpenFolder(_paths.BackgroundsPath));
        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(_paths.DataRoot));
        ResetAppearanceCommand = new RelayCommand(ResetAppearance);

        _background.PropertyChanged += OnBackgroundPropertyChanged;
    }

    public IReadOnlyList<AccentPreset> AccentPresets { get; }

    public IReadOnlyList<HomeWidgetToggle> HomeWidgets { get; }

    public ThemeMode ThemeMode
    {
        get => _settings.Current.Appearance.ThemeMode;
        set
        {
            if (_suppress || value == _settings.Current.Appearance.ThemeMode)
            {
                return;
            }

            _settings.Current.Appearance.ThemeMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            _theme.Apply();
            ScheduleSave();
        }
    }

    public bool IsSystemTheme
    {
        get => ThemeMode == ThemeMode.System;
        set
        {
            if (value)
            {
                ThemeMode = ThemeMode.System;
            }
        }
    }

    public bool IsDarkTheme
    {
        get => ThemeMode == ThemeMode.Dark;
        set
        {
            if (value)
            {
                ThemeMode = ThemeMode.Dark;
            }
        }
    }

    public bool IsLightTheme
    {
        get => ThemeMode == ThemeMode.Light;
        set
        {
            if (value)
            {
                ThemeMode = ThemeMode.Light;
            }
        }
    }

    public string AccentInput
    {
        get => _accentInput;
        set => SetProperty(ref _accentInput, value);
    }

    public string CurrentAccentHex => _settings.Current.Appearance.AccentColor;

    public double GlassOpacity
    {
        get => _settings.Current.Appearance.Glass.Opacity;
        set
        {
            if (_suppress)
            {
                return;
            }

            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(clamped - _settings.Current.Appearance.Glass.Opacity) < 0.001)
            {
                return;
            }

            _settings.Current.Appearance.Glass.Opacity = clamped;
            OnPropertyChanged();
            _theme.Apply();
            ScheduleSave();
        }
    }

    public double GlassBlur
    {
        get => _settings.Current.Appearance.Glass.Blur;
        set
        {
            if (_suppress)
            {
                return;
            }

            var clamped = Math.Clamp(value, 0, 40);
            if (Math.Abs(clamped - _settings.Current.Appearance.Glass.Blur) < 0.001)
            {
                return;
            }

            _settings.Current.Appearance.Glass.Blur = clamped;
            OnPropertyChanged();
            _background.Refresh();
            ScheduleSave();
        }
    }

    public double BackgroundDim
    {
        get => _settings.Current.Appearance.Background.Dim;
        set
        {
            if (_suppress)
            {
                return;
            }

            var clamped = Math.Clamp(value, 0, 0.9);
            if (Math.Abs(clamped - _settings.Current.Appearance.Background.Dim) < 0.001)
            {
                return;
            }

            _settings.Current.Appearance.Background.Dim = clamped;
            OnPropertyChanged();
            _background.Refresh();
            ScheduleSave();
        }
    }

    public double BackgroundBlur
    {
        get => _settings.Current.Appearance.Background.Blur;
        set
        {
            if (_suppress)
            {
                return;
            }

            var clamped = Math.Clamp(value, 0, 40);
            if (Math.Abs(clamped - _settings.Current.Appearance.Background.Blur) < 0.001)
            {
                return;
            }

            _settings.Current.Appearance.Background.Blur = clamped;
            OnPropertyChanged();
            _background.Refresh();
            ScheduleSave();
        }
    }

    public BackgroundFit BackgroundFit
    {
        get => _settings.Current.Appearance.Background.Fit;
        set
        {
            if (_suppress || value == _settings.Current.Appearance.Background.Fit)
            {
                return;
            }

            _settings.Current.Appearance.Background.Fit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFillFit));
            OnPropertyChanged(nameof(IsFitFit));
            _background.Refresh();
            ScheduleSave();
        }
    }

    public bool IsFillFit
    {
        get => BackgroundFit == QSLauncher.Core.Models.BackgroundFit.Fill;
        set
        {
            if (value)
            {
                BackgroundFit = QSLauncher.Core.Models.BackgroundFit.Fill;
            }
        }
    }

    public bool IsFitFit
    {
        get => BackgroundFit == QSLauncher.Core.Models.BackgroundFit.Fit;
        set
        {
            if (value)
            {
                BackgroundFit = QSLauncher.Core.Models.BackgroundFit.Fit;
            }
        }
    }

    public bool BackgroundMuted
    {
        get => _settings.Current.Appearance.Background.Muted;
        set
        {
            if (_suppress || value == _settings.Current.Appearance.Background.Muted)
            {
                return;
            }

            _settings.Current.Appearance.Background.Muted = value;
            OnPropertyChanged();
            _background.Refresh();
            ScheduleSave();
        }
    }

    public bool BackgroundLoop
    {
        get => _settings.Current.Appearance.Background.Loop;
        set
        {
            if (_suppress || value == _settings.Current.Appearance.Background.Loop)
            {
                return;
            }

            _settings.Current.Appearance.Background.Loop = value;
            OnPropertyChanged();
            _background.Refresh();
            ScheduleSave();
        }
    }

    public bool BackgroundPauseWhenInactive
    {
        get => _settings.Current.Appearance.Background.PauseWhenInactive;
        set
        {
            if (_suppress || value == _settings.Current.Appearance.Background.PauseWhenInactive)
            {
                return;
            }

            _settings.Current.Appearance.Background.PauseWhenInactive = value;
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public bool HasBackground => _background.HasCustomBackground;

    public bool IsVideoBackground => _settings.Current.Appearance.Background.Mode == BackgroundMode.Video;

    public string VersionText => AppInfo.Version;

    public string BackgroundModeText => _settings.Current.Appearance.Background.Mode switch
    {
        BackgroundMode.Image => "图片背景",
        BackgroundMode.Video => "视频背景",
        _ => "未设置背景"
    };

    public string BackgroundPathDisplay
    {
        get
        {
            var path = _settings.Current.Appearance.Background.Path;
            return string.IsNullOrWhiteSpace(path) ? "未选择文件" : Path.GetFileName(path);
        }
    }

    public string BackgroundPathFull
    {
        get
        {
            var path = _settings.Current.Appearance.Background.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                return _paths.BackgroundsPath;
            }

            try
            {
                return _paths.ResolveDataPath(path);
            }
            catch
            {
                return _paths.BackgroundsPath;
            }
        }
    }

    public string DataRoot => _paths.DataRoot;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    public ICommand SelectAccentCommand { get; }

    public ICommand ApplyAccentCommand { get; }

    public ICommand SelectImageCommand { get; }

    public ICommand SelectVideoCommand { get; }

    public ICommand ClearBackgroundCommand { get; }

    public ICommand SelectLogoCommand { get; }

    public ICommand ClearLogoCommand { get; }

    public ICommand OpenBackgroundsFolderCommand { get; }

    public ICommand OpenDataFolderCommand { get; }

    public ICommand ResetAppearanceCommand { get; }

    private void SelectBackground(BackgroundMode mode)
    {
        var filter = mode == BackgroundMode.Image
            ? "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*"
            : "视频文件|*.mp4;*.wmv;*.avi;*.mov;*.m4v|所有文件|*.*";
        var file = FileBrowserWindow.PickFile(
            mode == BackgroundMode.Image ? "选择背景图片" : "选择背景视频",
            filter);
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        try
        {
            var relative = _paths.ImportBackground(file, mode);
            var background = _settings.Current.Appearance.Background;
            background.Mode = mode;
            background.Path = relative;
            _background.Refresh();
            OnPropertyChanged(nameof(HasBackground));
            OnPropertyChanged(nameof(IsVideoBackground));
            OnPropertyChanged(nameof(BackgroundModeText));
            OnPropertyChanged(nameof(BackgroundPathDisplay));
            OnPropertyChanged(nameof(BackgroundPathFull));
            SetStatus(mode == BackgroundMode.Image ? "图片背景已应用。" : "视频背景已应用。", false);
            ScheduleSave();
        }
        catch (Exception exception)
        {
            AppLog.Error("导入背景失败", exception);
            SetStatus(exception.Message, true);
        }
    }

    public bool HasCustomLogo => _background.HasCustomLogo;

    public string LogoPathDisplay
    {
        get
        {
            var path = _settings.Current.Appearance.LogoPath;
            return string.IsNullOrWhiteSpace(path)
                ? "默认 QS 标识"
                : Path.GetFileName(path);
        }
    }

    private void SelectLogo()
    {
        var file = FileBrowserWindow.PickFile(
            "选择左上角 Logo",
            "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*");
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        try
        {
            _settings.Current.Appearance.LogoPath = _paths.ImportLogo(file);
            _background.Refresh();
            OnPropertyChanged(nameof(HasCustomLogo));
            OnPropertyChanged(nameof(LogoPathDisplay));
            SetStatus("Logo 已更新。", false);
            ScheduleSave();
        }
        catch (Exception exception)
        {
            AppLog.Error("导入 Logo 失败", exception);
            SetStatus(exception.Message, true);
        }
    }

    private void ClearLogo()
    {
        _settings.Current.Appearance.LogoPath = null;
        _background.Refresh();
        OnPropertyChanged(nameof(HasCustomLogo));
        OnPropertyChanged(nameof(LogoPathDisplay));
        SetStatus("已恢复默认 Logo。", false);
        ScheduleSave();
    }

    private void ClearBackground()
    {
        var background = _settings.Current.Appearance.Background;
        background.Mode = BackgroundMode.None;
        background.Path = null;
        _background.Refresh();
        OnPropertyChanged(nameof(HasBackground));
        OnPropertyChanged(nameof(IsVideoBackground));
        OnPropertyChanged(nameof(BackgroundModeText));
        OnPropertyChanged(nameof(BackgroundPathDisplay));
        OnPropertyChanged(nameof(BackgroundPathFull));
        SetStatus("已清除背景，原始文件仍保留在 data/backgrounds。", false);
        ScheduleSave();
    }

    private void ApplyAccent(string input)
    {
        if (!ColorMath.TryParseHex(input, out var color))
        {
            SetStatus("颜色格式无效，请输入 #RRGGBB 或 #AARRGGBB。", true);
            return;
        }

        var hex = color.ToHex();
        _settings.Current.Appearance.AccentColor = hex;
        AccentInput = hex;
        OnPropertyChanged(nameof(CurrentAccentHex));
        _theme.Apply();
        SetStatus("强调色已更新。", false);
        ScheduleSave();
    }

    private void ResetAppearance()
    {
        _suppress = true;
        try
        {
            _settings.Current.Appearance = new AppearanceSettings();
            _accentInput = _settings.Current.Appearance.AccentColor;
            _theme.Apply();
            _background.Refresh();
            OnPropertyChanged(string.Empty);
            SetStatus("外观设置已恢复默认值。", false);
        }
        finally
        {
            _suppress = false;
        }

        ScheduleSave();
    }

    private void OnBackgroundPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BackgroundService.HasCustomBackground)
            or nameof(BackgroundService.IsImage)
            or nameof(BackgroundService.IsVideo))
        {
            OnPropertyChanged(nameof(HasBackground));
        }
    }

    private void ScheduleSave()
    {
        if (_suppress)
        {
            return;
        }

        _saveCts?.Cancel();
        _saveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _saveCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, cts.Token).ConfigureAwait(false);
                await _settings.SaveAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A newer change is pending.
            }
            catch (Exception exception)
            {
                AppLog.Error("保存设置失败", exception);
            }
        });
    }

    private void SetStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
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
            // Non-fatal convenience action.
        }
    }
}
