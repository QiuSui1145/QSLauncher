using System.Windows;
using System.Windows.Input;
using QSLauncher.App.ViewModels;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.Views;

public partial class LoaderWindow : Window
{
    public LoaderWindow(
        LauncherContext context,
        string minecraftVersion,
        InstanceInfo? target,
        Action<InstanceInfo> installed)
    {
        InitializeComponent();
        DataContext = new LoaderInstallViewModel(
            context,
            minecraftVersion,
            target,
            instance =>
            {
                installed(instance);
                DialogResult = true;
            });
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
