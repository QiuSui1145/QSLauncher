using System.Collections.ObjectModel;
using System.Windows;
using QSLauncher.App.ViewModels;
using QSLauncher.Core.Models;

namespace QSLauncher.App.Services;

public sealed class DownloadTaskItem : ObservableObject
{
    private string _detail;
    private double _progress;
    private bool _isRunning;
    private bool _isFailed;
    private string _statusText;
    private DateTime? _finishedAt;

    public DownloadTaskItem(string title, string detail)
    {
        Id = Guid.NewGuid();
        Title = title;
        _detail = detail;
        _statusText = "进行中";
        _isRunning = true;
        StartedAt = DateTime.Now;
    }

    public Guid Id { get; }

    public string Title { get; }

    public DateTime StartedAt { get; }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public string ProgressText => IsRunning ? $"{Progress:0}%" : StatusText;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(TimeText));
            }
        }
    }

    public bool IsFailed
    {
        get => _isFailed;
        private set => SetProperty(ref _isFailed, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public string TimeText => _finishedAt is null
        ? $"{StartedAt:HH:mm} 开始"
        : $"{StartedAt:HH:mm} - {_finishedAt:HH:mm}";

    public void Report(DownloadProgress report)
    {
        Progress = report.Percent;
        Detail = string.IsNullOrWhiteSpace(report.Item) ? report.Stage : report.Item;
    }

    public void Complete(string detail)
    {
        Progress = 100;
        Detail = detail;
        StatusText = "已完成";
        IsRunning = false;
        _finishedAt = DateTime.Now;
        OnPropertyChanged(nameof(TimeText));
    }

    public void Fail(string message)
    {
        Detail = message;
        StatusText = "失败";
        IsRunning = false;
        IsFailed = true;
        _finishedAt = DateTime.Now;
        OnPropertyChanged(nameof(TimeText));
    }
}

/// <summary>
/// A process-wide record of downloads and long-running installs. The task
/// page stays useful even when the collection is empty.
/// </summary>
public sealed class TaskCenter
{
    private static readonly Lazy<TaskCenter> LazyInstance = new(() => new TaskCenter());

    private TaskCenter()
    {
    }

    public static TaskCenter Current => LazyInstance.Value;

    public ObservableCollection<DownloadTaskItem> Tasks { get; } = [];

    public DownloadTaskItem Begin(string title, string detail = "准备中…")
    {
        var item = new DownloadTaskItem(title, detail);
        RunOnUi(() => Tasks.Insert(0, item));
        return item;
    }

    public void Complete(DownloadTaskItem? item, string detail = "已完成")
    {
        if (item is null)
        {
            return;
        }

        RunOnUi(() => item.Complete(detail));
    }

    public void Fail(DownloadTaskItem? item, string message)
    {
        if (item is null)
        {
            return;
        }

        RunOnUi(() => item.Fail(message));
    }

    public void ClearFinished()
    {
        RunOnUi(() =>
        {
            foreach (var item in Tasks.Where(t => !t.IsRunning).ToList())
            {
                Tasks.Remove(item);
            }
        });
    }

    public IProgress<DownloadProgress> Relay(DownloadTaskItem item)
        => new Progress<DownloadProgress>(report => RunOnUi(() => item.Report(report)));

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
