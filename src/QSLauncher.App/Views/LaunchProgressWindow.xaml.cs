using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using QSLauncher.App.Services;
using QSLauncher.App.ViewModels;

namespace QSLauncher.App.Views;

public partial class LaunchProgressWindow : Window
{
    private readonly LogCenter _logs;
    private readonly DispatcherTimer _closeTimer;

    public LaunchProgressWindow(LogCenter logs)
    {
        InitializeComponent();
        _logs = logs;
        DataContext = logs;
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (!_logs.HasFailure)
            {
                Close();
            }
        };
        _logs.PropertyChanged += OnLogsPropertyChanged;
        Closed += (_, _) => _logs.PropertyChanged -= OnLogsPropertyChanged;
    }

    private void OnLogsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LogCenter.IsLaunching) || _logs.IsLaunching || _logs.HasFailure)
        {
            return;
        }

        _closeTimer.Stop();
        _closeTimer.Start();
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        if (Owner?.DataContext is MainViewModel main)
        {
            main.Navigate(PageKey.Logs);
        }

        Close();
    }
}
