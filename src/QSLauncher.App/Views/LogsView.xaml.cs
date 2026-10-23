using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace QSLauncher.App.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => Attach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => Attach();

    private void Attach()
    {
        if (LogList?.ItemsSource is not INotifyCollectionChanged collection)
        {
            return;
        }

        collection.CollectionChanged -= OnLinesChanged;
        collection.CollectionChanged += OnLinesChanged;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || LogList.Items.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[^1]);
            }
        });
    }
}
