using System.IO;
using System.Windows;
using System.Windows.Input;
using QSLauncher.App.Services;
using QSLauncher.App.ViewModels;
using QSLauncher.Core.Services;

namespace QSLauncher.App.Views;

public partial class InstanceSettingsWindow : Window
{
    private readonly InstanceItem _item;

    public InstanceSettingsWindow(InstanceItem item)
    {
        InitializeComponent();
        _item = item;
        DataContext = item;
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnBrowseJavaClick(object sender, RoutedEventArgs e)
    {
        var initial = string.IsNullOrWhiteSpace(_item.JavaPath)
            ? null
            : Path.GetDirectoryName(_item.JavaPath);
        var file = FileBrowserWindow.PickFile(
            "选择 java.exe",
            "Java 可执行文件 (java.exe)|java.exe|可执行文件 (*.exe)|*.exe",
            initial);
        if (!string.IsNullOrWhiteSpace(file))
        {
            _item.JavaPath = file;
        }
    }

    private void OnAutoMemoryClick(object sender, RoutedEventArgs e)
    {
        var recommendation = MemoryAdvisor.Recommend(_item.Info);
        _item.MemoryMb = recommendation.MemoryMb;
        AppDialog.Alert(
            "已智能分配内存",
            $"建议内存：{recommendation.MemoryMb} MB\n\n{recommendation.Reason}\n\n" +
            "保存后下次启动生效。");
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_item.Name))
        {
            AppDialog.Alert("实例设置", "实例名称不能为空。");
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
