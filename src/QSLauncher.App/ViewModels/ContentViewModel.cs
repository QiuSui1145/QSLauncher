using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using QSLauncher.App.Services;
using QSLauncher.App.Views;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public sealed class ContentViewModel : ObservableObject
{
    private readonly LauncherContext _context;
    private readonly InstanceInfo _instance;
    private ContentKind _kind = ContentKind.ResourcePacks;
    private ContentItem? _selectedItem;
    private string _status = "正在读取实例内容…";
    private bool _statusIsError;
    private bool _isBusy;

    public ContentViewModel(LauncherContext context, InstanceInfo instance)
    {
        _context = context;
        _instance = instance;
        Items = [];
        SelectKindCommand = new RelayCommand(parameter =>
        {
            if (parameter is string text && Enum.TryParse<ContentKind>(text, out var kind))
            {
                Kind = kind;
            }
        });
        ImportCommand = new AsyncRelayCommand(ImportAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        BackupCommand = new AsyncRelayCommand(BackupAsync);
        TogglePackCommand = new AsyncRelayCommand(TogglePackAsync);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        _ = LoadAsync();
    }

    public ObservableCollection<ContentItem> Items { get; }

    public InstanceInfo Instance => _instance;

    public string InstanceText => $"{_instance.DisplayName} · {_instance.Manifest.MinecraftVersion}";

    public ContentKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
            {
                OnPropertyChanged(nameof(KindTitle));
                OnPropertyChanged(nameof(KindHint));
                OnPropertyChanged(nameof(IsResourcePacks));
                OnPropertyChanged(nameof(IsShaderPacks));
                OnPropertyChanged(nameof(IsSaves));
                _ = LoadAsync();
            }
        }
    }

    public bool IsResourcePacks => Kind == ContentKind.ResourcePacks;

    public bool IsShaderPacks => Kind == ContentKind.ShaderPacks;

    public bool IsSaves => Kind == ContentKind.Saves;

    public string KindTitle => Kind switch
    {
        ContentKind.ResourcePacks => "资源包",
        ContentKind.ShaderPacks => "光影包",
        _ => "存档"
    };

    public string KindHint => Kind switch
    {
        ContentKind.ResourcePacks => "支持 ZIP 与文件夹；可在列表中启用/停用，进入世界后生效。",
        ContentKind.ShaderPacks => "支持 ZIP 与文件夹；Iris / OptiFine 在游戏内启用光影。",
        _ => "支持世界文件夹与 ZIP 备份；导入的解压包必须包含 level.dat。"
    };

    public ContentItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public bool HasItems => Items.Count > 0;

    public string EmptyText => Kind switch
    {
        ContentKind.ResourcePacks => "这个实例还没有资源包。",
        ContentKind.ShaderPacks => "这个实例还没有光影包。",
        _ => "这个实例还没有存档。"
    };

    public string FolderPath => _context.Contents.GetDirectory(_instance, Kind);

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

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public ICommand SelectKindCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand BackupCommand { get; }

    public ICommand TogglePackCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand RefreshCommand { get; }

    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = $"正在读取{KindTitle}…";
        try
        {
            var items = await _context.Contents.ListAsync(_instance, Kind).ConfigureAwait(true);
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            SelectedItem = Items.FirstOrDefault();
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(EmptyText));
            OnPropertyChanged(nameof(FolderPath));
            Status = Items.Count == 0
                ? EmptyText
                : $"共 {Items.Count} 个{KindTitle}。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"读取失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var filter = Kind switch
        {
            ContentKind.ResourcePacks => "资源包 (*.zip;*.mcpack)|*.zip;*.mcpack|所有文件 (*.*)|*.*",
            ContentKind.ShaderPacks => "光影包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
            _ => "世界存档 (*.zip)|*.zip|所有文件 (*.*)|*.*"
        };
        var file = FileBrowserWindow.PickFile($"导入{KindTitle}", filter);
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = $"正在导入{KindTitle}…";
        try
        {
            await _context.Contents.ImportAsync(_instance, Kind, file).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            Status = $"已导入{KindTitle}。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"导入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteAsync(object? parameter)
    {
        if (IsBusy)
        {
            return;
        }

        var item = parameter as ContentItem ?? SelectedItem;
        if (item is null)
        {
            return;
        }

        if (!AppDialog.Confirm(
                $"移除{KindTitle}",
                $"将“{item.Name}”移入隔离区？\n\n隔离区位于 data/quarantine，可在资源管理器中手动恢复。",
                "移入隔离区",
                danger: true))
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = "正在移入隔离区…";
        try
        {
            var destination = await _context.Contents.DeleteToQuarantineAsync(item)
                .ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            Status = $"已移入隔离区：{Path.GetFileName(destination)}";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"移除失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BackupAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var item = SelectedItem;
        if (item is null || !item.IsDirectory)
        {
            StatusIsError = true;
            Status = "先选择一个世界存档。";
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = "正在备份存档…";
        try
        {
            var destination = await _context.Contents.BackupSaveAsync(item)
                .ConfigureAwait(true);
            Status = $"已备份到 {destination}";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"备份失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TogglePackAsync(object? parameter)
    {
        if (IsBusy || parameter is not ContentItem item || item.Kind != ContentKind.ResourcePacks)
        {
            return;
        }

        var enable = !item.IsEnabled;
        IsBusy = true;
        StatusIsError = false;
        Status = enable ? "正在启用资源包…" : "正在停用资源包…";
        try
        {
            await _context.Contents.SetResourcePackEnabledAsync(
                _instance, item, enable).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            Status = $"{item.Name} 已{(enable ? "启用" : "停用")}；下次进入世界时生效。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = $"修改资源包状态失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{FolderPath}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening Explorer is a convenience.
        }
    }
}
