using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace QSLauncher.App.Views;

public partial class AppDialogWindow : Window
{
    public AppDialogWindow(
        string title,
        string message,
        string primaryText,
        string? secondaryText,
        bool danger = false)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;

        if (string.IsNullOrWhiteSpace(secondaryText))
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryButton.Content = secondaryText;
        }

        if (danger)
        {
            PrimaryButton.Style = (Style)FindResource("DangerButton");
        }

        Loaded += (_, _) =>
        {
            if (MessageText.ActualHeight > 300)
            {
                MessageText.MaxWidth = 380;
            }
        };
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // The mouse button was released before the drag started.
            }
        }
    }
}
