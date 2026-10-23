using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using QSLauncher.App.ViewModels;

namespace QSLauncher.App.Views;

public partial class AccountView : UserControl
{
    public AccountView()
    {
        InitializeComponent();
    }

    private void OnYggPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountsViewModel viewModel && sender is PasswordBox box)
        {
            viewModel.YggdrasilPassword = box.Password;
        }
    }

    private void OnOpenAzureClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade",
            UseShellExecute = true
        });
    }
}
