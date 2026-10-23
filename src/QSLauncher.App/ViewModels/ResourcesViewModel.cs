using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using QSLauncher.App.Services;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public sealed class ResourceItem
{
    public ResourceItem(ResourceCategory category)
    {
        Category = category;
    }

    public ResourceCategory Category { get; }

    public string Key => Category.Key;

    public string Name => Category.Name;

    public string Description => Category.Description;

    public string Path => Category.Path;

    public bool CanClean => Category.CanClean;

    public string SizeText => FormatBytes(Category.Bytes);

    public string FilesText => $"{Category.Files:N0} 个文件";

    public string CleanLabel => Key switch
    {
        "logs" => "清理日志",
        "cache" => "清空缓存",
        "downloads" => "清理暂存",
        _ => "清理"
    };

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d / 1024d:0.00} GB";
        }

        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        return bytes >= 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes} B";
    }
}

public sealed class ResourcesViewModel : ObservableObject
{
    private readonly LauncherContext _context;
    private readonly Action<PageKey> _navigate;
    private bool _isBusy;
    private string _status = "正在统计便携目录…";
    private bool _statusIsError;
    private InstanceItem? _selectedInstance;

    public ResourcesViewModel(LauncherContext context, Action<PageKey> navigate)
    {
        _context = context;
        _navigate = navigate;
        Categories = [];
        Instances = [];
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CleanCommand = new AsyncRelayCommand(CleanAsync);
        OpenCommand = new RelayCommand(Open);
        OpenContentCommand = new RelayCommand(OpenContent);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync);
        _ = LoadAsync();
    }

    public Task RefreshAsync() => LoadAsync();

    public ObservableCollection<ResourceItem> Categories { get; }

    public ObservableCollection<InstanceItem> Instances { get; }

    public InstanceItem? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (SetProperty(ref _selectedInstance, value))
            {
                OnPropertyChanged(nameof(HasSelectedInstance));
            }
        }
    }

    public bool HasSelectedInstance => SelectedInstance is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

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

    public string TotalText
    {
        get
        {
            var bytes = Categories.Sum(c => c.Category.Bytes);
            return ResourceItem.FormatBytes(bytes);
        }
    }

    public string TotalFilesText => $"{Categories.Sum(c => c.Category.Files):N0} 个文件";

    public string DataRoot => _context.Paths.DataRoot;

    public ICommand RefreshCommand { get; }

    public ICommand CleanCommand { get; }

    public ICommand OpenCommand { get; }

    public ICommand OpenContentCommand { get; }

    public ICommand VerifyCommand { get; }

    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = "正在统计便携目录…";
        try
        {
            var stats = await _context.Resources.GetStatsAsync().ConfigureAwait(true);
            Categories.Clear();
            foreach (var category in stats)
            {
                Categories.Add(new ResourceItem(category));
            }

            var selectedId = SelectedInstance?.Info.Manifest.Id;
            var instances = await _context.Instances.ListAsync().ConfigureAwait(true);
            Instances.Clear();
            foreach (var instance in instances.OrderBy(i => i.DisplayName))
            {
                Instances.Add(new InstanceItem(instance));
            }

            SelectedInstance = Instances.FirstOrDefault(i => i.Info.Manifest.Id == selectedId)
                               ?? Instances.FirstOrDefault();

            OnPropertyChanged(nameof(TotalText));
            OnPropertyChanged(nameof(TotalFilesText));
            Status = $"已统计 {Categories.Count} 个目录，所有数据都位于程序目录内。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"统计失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CleanAsync(object? parameter)
    {
        if (parameter is not ResourceItem item || IsBusy || !item.CanClean)
        {
            return;
        }

        if (!AppDialog.Confirm(
                item.CleanLabel,
                $"确定要{item.CleanLabel}吗？\n\n目标：{item.Path}\n\n这只删除可重新生成的文件，不会删除实例、存档或已安装的模组。",
                item.CleanLabel,
                danger: true))
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = $"正在{item.CleanLabel}…";
        try
        {
            var deleted = await _context.Resources.CleanAsync(item.Key).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            Status = $"已清理 {deleted} 个文件。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"清理失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task VerifyAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (SelectedInstance is null)
        {
            StatusIsError = true;
            Status = "先选择一个实例，才能校验它的客户端、依赖库与资源。";
            return;
        }

        if (!AppDialog.Confirm(
                "校验与修复",
                $"校验并修复“{SelectedInstance.Name}”的游戏文件？\n\n启动器会重新检查客户端、依赖库与资源索引，只重新下载缺失或哈希不符的文件。",
                "开始校验"))
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = "正在校验客户端、依赖库与资源…";
        var task = TaskCenter.Current.Begin($"校验 {SelectedInstance.Name}", "检查客户端、依赖库与资源");
        try
        {
            var progress = new Progress<DownloadProgress>(value =>
            {
                Status = string.IsNullOrWhiteSpace(value.Item)
                    ? $"校验中… {value.Percent}%"
                    : $"{value.Stage}：{value.Item}";
                task.Report(value);
            });
            await _context.Installer.InstallAsync(SelectedInstance.Info, progress)
                .ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            Status = $"“{SelectedInstance.Name}”校验完成，正常文件未被重复下载。";
            TaskCenter.Current.Complete(task, "校验完成");
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"校验失败：{ex.Message}";
            TaskCenter.Current.Fail(task, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenContent()
    {
        if (SelectedInstance is null)
        {
            StatusIsError = true;
            Status = "先创建一个实例，才能管理资源包、光影与存档。";
            return;
        }

        var window = new Views.ContentWindow(_context, SelectedInstance.Info)
        {
            Owner = Application.Current?.MainWindow
        };
        window.ShowDialog();
        _ = LoadAsync();
    }

    private void Open(object? parameter)
    {
        var path = parameter is ResourceItem item ? item.Path : _context.Paths.DataRoot;
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
            // Opening a folder is a convenience.
        }
    }
}
