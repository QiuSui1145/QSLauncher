using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using QSLauncher.App.Services;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public sealed class LogsViewModel : ObservableObject
{
    private readonly LogCenter _logs;
    private readonly PortablePaths _paths;
    private string _status = "等待游戏启动。";
    private bool _statusIsError;

    public LogsViewModel(LogCenter logs, PortablePaths paths)
    {
        _logs = logs;
        _paths = paths;
        ExportCommand = new AsyncRelayCommand(ExportAsync);
        ClearCommand = new RelayCommand(ClearDisplay);
        _logs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LogCenter.HasLines) or nameof(LogCenter.HasFindings))
            {
                OnPropertyChanged(nameof(EmptyHint));
            }
        };
        OpenFolderCommand = new RelayCommand(() => OpenFolder(_paths.LogsPath));
        RefreshCommand = new RelayCommand(() =>
        {
            Status = _logs.HasLines ? "日志已同步。" : "当前没有游戏日志，启动游戏后会实时显示。";
            StatusIsError = false;
        });
    }

    public LogCenter Logs => _logs;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public string EmptyHint => _logs.HasLines
        ? string.Empty
        : "启动游戏后，这里会显示登录、校验、补全文件以及游戏输出的完整日志。";

    public ICommand ExportCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand RefreshCommand { get; }

    private async Task ExportAsync()
    {
        try
        {
            StatusIsError = false;
            Status = "正在打包完整日志…";
            var path = await _logs.ExportZipAsync().ConfigureAwait(true);
            Status = $"日志 ZIP 已导出：{path}";
            AppDialog.Alert("日志已导出", $"完整日志已保存为：\n\n{path}\n\n可以直接把这个 ZIP 发送给他人排查。");
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"导出失败：{ex.Message}";
            AppLog.Error("导出日志失败", ex);
        }
    }

    private void ClearDisplay()
    {
        _logs.ClearDisplay();
        Status = "已清空当前显示；磁盘上的历史日志仍然保留，可随时导出。";
        StatusIsError = false;
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // Convenience only.
        }
    }
}
