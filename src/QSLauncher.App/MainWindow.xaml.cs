using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QSLauncher.App.Interop;
using QSLauncher.App.Services;
using QSLauncher.App.ViewModels;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly BackgroundService _background;

    public MainWindow(
        PortablePaths paths,
        SettingsService settings,
        ThemeService theme,
        BackgroundService background,
        PageKey initialPage = PageKey.Home)
    {
        InitializeComponent();
        _settings = settings;
        _theme = theme;
        _background = background;

        var viewModel = new MainViewModel(paths, settings, theme, background);
        DataContext = viewModel;
        ApplySavedBounds(settings.Current.Window);
        Loaded += async (_, _) =>
        {
            viewModel.Navigate(initialPage);

            // Home reads the instance library, so the library has to be loaded
            // even when the user never opens the versions page first.
            if (initialPage != PageKey.Versions)
            {
                await viewModel.Versions.RefreshAsync();
            }

            viewModel.Home.Refresh();
        };

        _theme.ThemeChanged += (_, _) => ApplyWindowTheme();
        _background.PropertyChanged += OnBackgroundPropertyChanged;
        PageHost.SizeChanged += (_, _) => LogLayout();
        Loaded += (_, _) => LogLayout();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyWindowTheme();
    }

    private void ApplyWindowTheme()
    {
        try
        {
            WindowEffects.Apply(this, dark: !_theme.IsLight, enableMica: false);
        }
        catch
        {
            // Visual polish must never prevent the window from opening.
        }
    }

    private void ApplySavedBounds(WindowSettings saved)
    {
        var workArea = SystemParameters.WorkArea;
        var dpiScale = 1.0;
        try
        {
            dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        }
        catch
        {
            // The window may not have a presentation source yet.
        }

        if (dpiScale <= 0.1)
        {
            dpiScale = 1.0;
        }

        Width = Math.Clamp(
            double.IsFinite(saved.Width) ? saved.Width : 1280,
            MinWidth,
            Math.Max(MinWidth, workArea.Width / dpiScale - 24));
        Height = Math.Clamp(
            double.IsFinite(saved.Height) ? saved.Height : 800,
            MinHeight,
            Math.Max(MinHeight, workArea.Height / dpiScale - 24));

        if (double.IsFinite(saved.Left) && double.IsFinite(saved.Top))
        {
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
            // Keep the whole window on screen. Clamping only a corner used to
            // push the right and bottom edges outside the work area on scaled
            // displays, which made pages look clipped.
            var left = Math.Clamp(
                saved.Left, virtualLeft,
                Math.Max(virtualLeft, virtualRight - Math.Min(Width, SystemParameters.VirtualScreenWidth)));
            var top = Math.Clamp(
                saved.Top, virtualTop,
                Math.Max(virtualTop, virtualBottom - Math.Min(Height, SystemParameters.VirtualScreenHeight)));
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        if (saved.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (e.ButtonState != MouseButtonState.Pressed || FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse was released before the drag started.
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e)
        => Close();

    private void OnTasksClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Navigate(PageKey.Tasks);
        }
    }

    private void OnAccountClick(object sender, RoutedEventArgs e)
    {
        AccountMenu.IsOpen = !AccountMenu.IsOpen;
    }

    private void OnAccountMenuSelect(object sender, RoutedEventArgs e)
    {
        AccountMenu.IsOpen = false;
        if (sender is Button { DataContext: AccountItem account }
            && DataContext is MainViewModel viewModel)
        {
            viewModel.Accounts.SelectAccountCommand.Execute(account);
        }
    }

    private void OnManageAccounts(object sender, RoutedEventArgs e)
    {
        AccountMenu.IsOpen = false;
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Navigate(PageKey.Account);
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeIcon is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Data = (Geometry)(TryFindResource(
            maximized ? "IconRestore" : "IconMaximize") ?? Geometry.Empty);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_settings.Current.Appearance.Background.PauseWhenInactive)
        {
            BackgroundVideo?.Resume();
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (_settings.Current.Appearance.Background.PauseWhenInactive)
        {
            BackgroundVideo?.Pause();
        }
    }

    private void OnBackgroundPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackgroundService.IsVideo) && BackgroundVideo is not null)
        {
            if (_background.IsVideo && IsActive)
            {
                BackgroundVideo.Resume();
            }
        }
    }

    private void OnWindowPreviewDragOver(object sender, DragEventArgs e)
    {
        var accepted = FindDroppedModpack(e.Data) is not null;
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnWindowPreviewDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        var archivePath = FindDroppedModpack(e.Data);
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (archivePath is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var instance = viewModel.Versions.SelectedInstance;
        if (instance is null)
        {
            AppDialog.Alert(
                "请先选择实例",
                "拖入整合包前，请先在“版本管理”中选中一个要安装到的实例。");
            return;
        }

        var confirmed = AppDialog.Confirm(
            "安装整合包",
            $"将 {Path.GetFileName(archivePath)} 安装到当前实例「{instance.Name}」？\n\n" +
            "整合包中的配置、模组和资源会覆盖到当前挂载目录，同名文件将被替换。",
            "安装到当前实例");
        if (!confirmed)
        {
            return;
        }

        viewModel.Navigate(PageKey.Versions);
        await viewModel.Versions.InstallModpackOverlayAsync(archivePath);
    }

    private static string? FindDroppedModpack(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop)
            || data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return null;
        }

        return files.FirstOrDefault(path =>
        {
            var extension = Path.GetExtension(path);
            return File.Exists(path)
                   && (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                       || extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase));
        });
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        var saved = _settings.Current.Window;
        saved.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            saved.Width = Width;
            saved.Height = Height;
            saved.Left = Left;
            saved.Top = Top;
        }

        try
        {
            _settings.SaveAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Closing should not be blocked by a settings write failure.
        }
    }

    private void LogLayout()
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var page = PageHost.Content as FrameworkElement;
            var work = SystemParameters.WorkArea;
            AppLog.Info(
                $"布局 window={ActualWidth:0}x{ActualHeight:0} at {Left:0},{Top:0} " +
                $"host={PageHost.ActualWidth:0} " +
                $"page={page?.ActualWidth:0}x{page?.ActualHeight:0} " +
                $"dpi={dpi.DpiScaleX:0.##} work={work.Width:0}x{work.Height:0} " +
                $"screen={SystemParameters.VirtualScreenWidth:0}x{SystemParameters.VirtualScreenHeight:0}");
        }
        catch
        {
            // Diagnostics must not affect the UI.
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is FrameworkContentElement contentElement
                ? contentElement.Parent
                : VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
