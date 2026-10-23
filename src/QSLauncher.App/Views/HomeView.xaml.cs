using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using QSLauncher.App.ViewModels;

namespace QSLauncher.App.Views;

public partial class HomeView : UserControl
{
    private HomeWidgetOption? _dragging;
    private Point _dragStart;
    private double _startX;
    private double _startY;
    private FrameworkElement? _dragSurface;

    public HomeView()
    {
        InitializeComponent();
    }

    private HomeWidgetOption? FindWidget(object sender)
    {
        if (DataContext is not HomeViewModel home || sender is not FrameworkElement element
            || element.Tag is not string key)
        {
            return null;
        }

        return key switch
        {
            "launch" => home.LaunchWidget,
            "quick" => home.QuickWidget,
            "stats" => home.StatsWidget,
            "recent" => home.RecentWidget,
            _ => null
        };
    }

    private void OnWidgetDragStart(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not HomeViewModel { IsEditingLayout: true })
        {
            return;
        }

        var widget = FindWidget(sender);
        if (widget is null || sender is not FrameworkElement element)
        {
            return;
        }

        _dragging = widget;
        _dragSurface = element;
        _dragStart = e.GetPosition(HomeCanvas);
        _startX = widget.X;
        _startY = widget.Y;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void OnWidgetDragMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(HomeCanvas);
        var maxX = Math.Max(0, HomeCanvas.ActualWidth - _dragging.Width);
        var maxY = Math.Max(0, HomeCanvas.ActualHeight - _dragging.Height);
        _dragging.X = Math.Clamp(_startX + (point.X - _dragStart.X), 0, maxX);
        _dragging.Y = Math.Clamp(_startY + (point.Y - _dragStart.Y), 0, maxY);
        e.Handled = true;
    }

    private void OnWidgetDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is null)
        {
            return;
        }

        _dragSurface?.ReleaseMouseCapture();
        _dragging = null;
        _dragSurface = null;
        if (DataContext is HomeViewModel home)
        {
            _ = home.SaveLayoutAsync();
        }

        e.Handled = true;
    }

    private void OnWidgetResize(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not HomeViewModel { IsEditingLayout: true }
            || sender is not FrameworkElement element
            || element.Tag is not string key)
        {
            return;
        }

        var home = (HomeViewModel)DataContext;
        var widget = key switch
        {
            "launch" => home.LaunchWidget,
            "quick" => home.QuickWidget,
            "stats" => home.StatsWidget,
            "recent" => home.RecentWidget,
            _ => null
        };
        if (widget is null)
        {
            return;
        }

        var maxWidth = Math.Max(260, HomeCanvas.ActualWidth - widget.X);
        var maxHeight = Math.Max(100, HomeCanvas.ActualHeight - widget.Y);
        widget.Width = Math.Min(widget.Width + e.HorizontalChange, maxWidth);
        widget.Height = Math.Min(widget.Height + e.VerticalChange, maxHeight);
        e.Handled = true;
    }

    private void OnWidgetResizeEnd(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is HomeViewModel home)
        {
            _ = home.SaveLayoutAsync();
        }
    }
}
