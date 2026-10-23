using System.Windows;
using QSLauncher.App.Views;

namespace QSLauncher.App.Services;

/// <summary>
/// All in-app prompts use the launcher's own window chrome. Native Windows
/// message boxes are intentionally never used.
/// </summary>
public static class AppDialog
{
    public static bool Confirm(
        string title,
        string message,
        string confirmText = "确定",
        bool danger = false,
        string cancelText = "取消")
    {
        return Show(title, message, confirmText, cancelText, danger);
    }

    public static void Alert(string title, string message, string button = "知道了")
    {
        Show(title, message, button, null, false);
    }

    private static bool Show(
        string title,
        string message,
        string primaryText,
        string? secondaryText,
        bool danger)
    {
        var application = Application.Current;
        if (application is null)
        {
            return false;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            return application.Dispatcher.Invoke(
                () => Show(title, message, primaryText, secondaryText, danger));
        }

        var owner = application.MainWindow;
        var window = new AppDialogWindow(title, message, primaryText, secondaryText, danger);
        if (owner is not null && owner.IsLoaded && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true;
    }
}
