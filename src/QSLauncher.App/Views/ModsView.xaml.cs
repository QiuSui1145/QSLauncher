using System.Windows;
using System.Windows.Controls;
using QSLauncher.App.ViewModels;

namespace QSLauncher.App.Views;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DataContext is ModsViewModel { HasInstance: true }
                    && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not ModsViewModel viewModel
            || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        e.Handled = true;
        await viewModel.ImportFilesAsync(files);
    }
}
