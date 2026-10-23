using System.Windows;
using System.Windows.Threading;
using QSLauncher.App.Services;
using QSLauncher.App.ViewModels;
using QSLauncher.Core.Services;

namespace QSLauncher.App;

public partial class App : Application
{
    private PortablePaths? _paths;
    private bool _errorDialogVisible;
    private string? _lastErrorSignature;
    private DateTime _lastErrorAt;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppLog.Error("未处理的后台异常", exception);
            }
        };

        try
        {
            _paths = new PortablePaths();
            _paths.EnsureLayout();
            AppLog.Initialize(_paths.LogsPath);

            var settings = new SettingsService(_paths);
            await settings.LoadAsync();

            var theme = new ThemeService(settings);
            theme.Initialize();

            var background = new BackgroundService(_paths, settings);
            background.Refresh();

            var initialPage = PageKey.Home;
            foreach (var argument in e.Args)
            {
                if (argument.StartsWith("--page=", StringComparison.OrdinalIgnoreCase)
                    && Enum.TryParse<PageKey>(argument["--page=".Length..], true, out var page))
                {
                    initialPage = page;
                }
            }

            var window = new MainWindow(_paths, settings, theme, background, initialPage);
            MainWindow = window;
            window.Show();
            AppLog.Info($"QS Launcher 启动，数据目录: {_paths.DataRoot}");
        }
        catch (Exception exception)
        {
            AppLog.Error("启动失败", exception);
            AppDialog.Alert(
                "启动失败",
                "QS Launcher 无法启动。\n\n" +
                "请确认程序所在目录可写，且没有被杀毒软件拦截。\n\n" +
                exception.Message);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _paths?.EnsureLayout();
        }
        catch
        {
            // The process is already exiting.
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("界面线程异常", e.Exception);
        e.Handled = true;

        // A failing layout pass can raise the same exception many times per
        // frame. Show one in-app notice, not a stack of modal windows.
        var signature = $"{e.Exception.GetType().FullName}:{e.Exception.Message}";
        if (_errorDialogVisible)
        {
            return;
        }

        if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal)
            && DateTime.UtcNow - _lastErrorAt < TimeSpan.FromSeconds(8))
        {
            return;
        }

        _lastErrorSignature = signature;
        _lastErrorAt = DateTime.UtcNow;
        _errorDialogVisible = true;
        try
        {
            AppDialog.Alert(
                "界面发生错误",
                "启动器已记录这次错误，可以继续使用其余页面。\n\n" +
                e.Exception.Message +
                "\n\n完整日志位于 data\\logs。");
        }
        catch (Exception dialogError)
        {
            AppLog.Error("显示错误提示失败", dialogError);
        }
        finally
        {
            _errorDialogVisible = false;
        }
    }
}
