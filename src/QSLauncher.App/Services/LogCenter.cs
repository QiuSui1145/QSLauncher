using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Windows;
using QSLauncher.App.ViewModels;
using QSLauncher.Core.Services;

namespace QSLauncher.App.Services;

public enum LaunchStageState
{
    Pending,
    Running,
    Done,
    Failed
}

public sealed class LaunchStage : ObservableObject
{
    private LaunchStageState _state;
    private string _detail = string.Empty;

    public LaunchStage(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }

    public string Title { get; }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public LaunchStageState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(Marker));
            }
        }
    }

    public bool IsRunning => State == LaunchStageState.Running;

    public bool IsDone => State == LaunchStageState.Done;

    public bool IsFailed => State == LaunchStageState.Failed;

    public string Marker => State switch
    {
        LaunchStageState.Done => "✓",
        LaunchStageState.Failed => "!",
        LaunchStageState.Running => "·",
        _ => "○"
    };
}

public sealed class LogLine
{
    public LogLine(string text, bool isError = false)
    {
        Text = text;
        IsError = isError;
        Time = DateTimeOffset.Now;
    }

    public DateTimeOffset Time { get; }

    public string TimeText => Time.ToString("HH:mm:ss");

    public string Text { get; }

    public bool IsError { get; }
}

/// <summary>
/// In-memory log for the log page and the launch progress popup. A new launch
/// clears the previous UI log, while the files on disk remain available for
/// ZIP export.
/// </summary>
public sealed class LogCenter : ObservableObject
{
    private const int MaxLines = 6000;
    private readonly HashSet<string> _findingKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _logsRoot;
    private string _instanceName = "尚未启动游戏";
    private string? _currentLogPath;
    private bool _isLaunching;
    private string _status = "等待启动游戏。启动后，游戏输出会实时同步到这里。";
    private string _failure = string.Empty;

    public LogCenter(string logsRoot)
    {
        _logsRoot = logsRoot;
        Stages =
        [
            new("login", "登录账号"),
            new("verify", "校验版本文件"),
            new("fill", "补全缺失文件"),
            new("starting", "等待游戏启动")
        ];
    }

    public ObservableCollection<LogLine> Lines { get; } = [];

    public ObservableCollection<LogFinding> Findings { get; } = [];

    public ObservableCollection<LaunchStage> Stages { get; }

    public string InstanceName
    {
        get => _instanceName;
        private set => SetProperty(ref _instanceName, value);
    }

    public string? CurrentLogPath
    {
        get => _currentLogPath;
        private set
        {
            if (SetProperty(ref _currentLogPath, value))
            {
                OnPropertyChanged(nameof(CurrentLogText));
            }
        }
    }

    public string CurrentLogText => string.IsNullOrWhiteSpace(CurrentLogPath)
        ? "尚未生成日志文件"
        : CurrentLogPath;

    public bool IsLaunching
    {
        get => _isLaunching;
        private set
        {
            if (SetProperty(ref _isLaunching, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !IsLaunching;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string Failure
    {
        get => _failure;
        private set
        {
            if (SetProperty(ref _failure, value))
            {
                OnPropertyChanged(nameof(HasFailure));
            }
        }
    }

    public bool HasFailure => !string.IsNullOrWhiteSpace(Failure);

    public bool HasLines => Lines.Count > 0;

    public bool HasFindings => Findings.Count > 0;

    public string FindingSummary => Findings.Count == 0
        ? "尚未发现明显问题"
        : $"发现 {Findings.Count} 条可能的问题";

    public void BeginLaunch(string instanceName)
    {
        RunOnUi(() =>
        {
            InstanceName = instanceName;
            CurrentLogPath = null;
            Failure = string.Empty;
            IsLaunching = true;
            Lines.Clear();
            Findings.Clear();
            _findingKeys.Clear();
            OnPropertyChanged(nameof(HasLines));
            OnPropertyChanged(nameof(HasFindings));
            OnPropertyChanged(nameof(FindingSummary));
            Status = "开始启动流程…";
            foreach (var stage in Stages)
            {
                stage.State = LaunchStageState.Pending;
                stage.Detail = string.Empty;
            }

            Stages[0].State = LaunchStageState.Running;
            Stages[0].Detail = "正在准备账号凭据";
            AppendLauncher($"开始启动实例：{instanceName}");
        });
    }

    public void SetStage(string key, LaunchStageState state, string? detail = null)
    {
        RunOnUi(() =>
        {
            var stage = Stages.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (stage is null)
            {
                return;
            }

            stage.State = state;
            if (!string.IsNullOrWhiteSpace(detail))
            {
                stage.Detail = detail;
            }

            Status = detail ?? stage.Title;
            if (state == LaunchStageState.Running)
            {
                AppendLauncher($"{stage.Title}：{detail ?? "进行中"}");
            }
            else if (state == LaunchStageState.Done)
            {
                AppendLauncher($"{stage.Title}：完成{(string.IsNullOrWhiteSpace(detail) ? string.Empty : $" · {detail}")}");
            }
            else if (state == LaunchStageState.Failed)
            {
                AppendLauncher($"{stage.Title}：失败{(string.IsNullOrWhiteSpace(detail) ? string.Empty : $" · {detail}")}", true);
            }
        });
    }

    public void SetProgress(string detail)
    {
        RunOnUi(() =>
        {
            var stage = Stages.FirstOrDefault(item => item.Key == "fill");
            if (stage is not null && stage.State == LaunchStageState.Running)
            {
                stage.Detail = detail;
            }

            Status = detail;
        });
    }

    public void AttachLogFile(string path)
    {
        RunOnUi(() =>
        {
            CurrentLogPath = path;
            Status = "游戏进程已启动，正在同步日志…";
        });
    }

    public void Append(string? line, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        RunOnUi(() =>
        {
            Lines.Add(new LogLine(line, isError || IsErrorLine(line)));
            while (Lines.Count > MaxLines)
            {
                Lines.RemoveAt(0);
            }

            OnPropertyChanged(nameof(HasLines));
            Analyze(line);
        });
    }

    public void AppendLauncher(string message, bool isError = false)
    {
        Append($"[启动器] {message}", isError);
    }

    public void Fail(string message)
    {
        RunOnUi(() =>
        {
            IsLaunching = false;
            Failure = message;
            Status = $"启动失败：{message}";
            var running = Stages.FirstOrDefault(stage => stage.State == LaunchStageState.Running);
            if (running is not null)
            {
                running.State = LaunchStageState.Failed;
                running.Detail = message;
            }

            AppendLauncher($"启动失败：{message}", true);
            if (_findingKeys.Add("launch-failure|"))
            {
                Findings.Add(new LogFinding(
                    LogIssueSeverity.Error,
                    "启动流程中断",
                    message,
                    "请检查登录状态、版本目录、Java 运行时与网络连接；上方启动阶段标出了中断位置，可导出日志进一步排查。"));
                OnPropertyChanged(nameof(HasFindings));
                OnPropertyChanged(nameof(FindingSummary));
            }
        });
    }

    public void CompleteLaunch()
    {
        RunOnUi(() =>
        {
            foreach (var stage in Stages)
            {
                if (stage.State is LaunchStageState.Pending or LaunchStageState.Running)
                {
                    stage.State = LaunchStageState.Done;
                }
            }

            IsLaunching = false;
            Status = "游戏已启动，日志会继续同步。";
            AppendLauncher("启动完成，开始等待游戏日志。");
        });
    }

    public void ClearDisplay()
    {
        RunOnUi(() =>
        {
            Lines.Clear();
            Findings.Clear();
            _findingKeys.Clear();
            Failure = string.Empty;
            OnPropertyChanged(nameof(HasLines));
            OnPropertyChanged(nameof(HasFindings));
            OnPropertyChanged(nameof(FindingSummary));
        });
    }

    public void MarkExited(int exitCode)
    {
        RunOnUi(() =>
        {
            IsLaunching = false;
            Status = exitCode == 0
                ? "游戏已正常退出。"
                : $"游戏已退出（退出码 {exitCode}），请查看问题诊断。";
            AppendLauncher(Status, exitCode != 0);
        });
    }

    public async Task<string> ExportZipAsync(CancellationToken cancellationToken = default)
    {
        var source = LogsRoot();
        var exports = Path.Combine(source, "exports");
        Directory.CreateDirectory(exports);
        var target = Path.Combine(
            exports,
            $"QSLauncher-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(target, ZipArchiveMode.Create);
            if (!Directory.Exists(source))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.StartsWith(exports, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var entryName = Path.GetRelativePath(source, file);
                    archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                }
                catch (IOException)
                {
                    // A log can be locked briefly while the game is writing.
                }
            }
        }, cancellationToken).ConfigureAwait(false);
        return target;
    }

    public string LogsRoot() => _logsRoot;

    private void Analyze(string line)
    {
        var finding = LogAnalyzer.Analyze(line);
        if (finding is null)
        {
            return;
        }

        var key = $"{finding.Title}|{finding.ModName}";
        if (!_findingKeys.Add(key))
        {
            return;
        }

        Findings.Add(finding);
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(FindingSummary));
    }

    private static bool IsErrorLine(string line)
    {
        return line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
               || line.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
               || line.Contains("Exception", StringComparison.OrdinalIgnoreCase)
               || line.Contains("Caused by:", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
