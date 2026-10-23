using System.Windows;
using QSLauncher.App.ViewModels;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.Views;

public partial class ContentWindow : Window
{
    public ContentWindow(LauncherContext context, InstanceInfo instance)
    {
        InitializeComponent();
        DataContext = new ContentViewModel(context, instance);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
