using System.Windows;
using System.Windows.Input;
using QSLauncher.App.Services;

namespace QSLauncher.App.Views;

public partial class TextPromptWindow : Window
{
    private readonly Func<string, string?>? _validate;

    private TextPromptWindow(
        string title,
        string prompt,
        string initialValue,
        Func<string, string?>? validate)
    {
        InitializeComponent();
        _validate = validate;
        TitleText.Text = title;
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    public static string? Show(
        string title,
        string prompt,
        string initialValue = "",
        Func<string, string?>? validate = null)
    {
        var application = Application.Current;
        if (application is null)
        {
            return null;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            return application.Dispatcher.Invoke(() => Show(title, prompt, initialValue, validate));
        }

        var window = new TextPromptWindow(title, prompt, initialValue, validate);
        if (application.MainWindow is { IsLoaded: true } owner)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true ? window.Value : null;
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnValueKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnConfirmClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var value = Value;
        if (value.Length == 0)
        {
            ErrorText.Text = "名称不能为空。";
            return;
        }

        var error = _validate?.Invoke(value);
        if (!string.IsNullOrWhiteSpace(error))
        {
            ErrorText.Text = error;
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
