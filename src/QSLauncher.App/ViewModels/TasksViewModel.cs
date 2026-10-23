using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using QSLauncher.App.Services;

namespace QSLauncher.App.ViewModels;

public sealed class TasksViewModel : ObservableObject
{
    private readonly TaskCenter _center;

    public TasksViewModel(TaskCenter center)
    {
        _center = center;
        Tasks = center.Tasks;
        Tasks.CollectionChanged += OnTasksChanged;
        foreach (var item in Tasks)
        {
            item.PropertyChanged += OnTaskPropertyChanged;
        }

        ClearFinishedCommand = new RelayCommand(
            () => _center.ClearFinished(),
            () => Tasks.Any(t => !t.IsRunning));
    }

    public ObservableCollection<DownloadTaskItem> Tasks { get; }

    public bool HasTasks => Tasks.Count > 0;

    public bool HasActive => Tasks.Any(t => t.IsRunning);

    public int ActiveCount => Tasks.Count(t => t.IsRunning);

    public string Summary => HasTasks
        ? HasActive
            ? $"{ActiveCount} 个任务进行中 · {Tasks.Count} 条记录"
            : $"{Tasks.Count} 条任务记录 · 当前没有进行中的任务"
        : "暂无下载任务";

    public string EmptyTitle => "暂无下载任务";

    public string EmptyDetail => "版本下载、加载器安装、整合包导入、模组安装与资源校验会自动出现在这里。";

    public ICommand ClearFinishedCommand { get; }

    public void Refresh()
    {
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(HasActive));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(Summary));
        if (ClearFinishedCommand is RelayCommand relay)
        {
            relay.RaiseCanExecuteChanged();
        }
    }

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DownloadTaskItem item in e.OldItems)
            {
                item.PropertyChanged -= OnTaskPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DownloadTaskItem item in e.NewItems)
            {
                item.PropertyChanged += OnTaskPropertyChanged;
            }
        }

        Refresh();
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadTaskItem.IsRunning)
            or nameof(DownloadTaskItem.StatusText))
        {
            Refresh();
        }
    }
}
