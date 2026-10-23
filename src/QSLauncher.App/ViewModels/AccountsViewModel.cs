using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using QSLauncher.App.Services;
using QSLauncher.App.Views;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.ViewModels;

public sealed class AccountItem : ObservableObject
{
    private bool _isSelected;

    public AccountItem(StoredAccount stored)
    {
        Stored = stored;
    }

    public StoredAccount Stored { get; }

    public string Id => Stored.Id;

    public string Name => Stored.Name;

    public string KindText => Stored.Kind switch
    {
        AccountKind.Microsoft => "微软账户",
        AccountKind.Yggdrasil => "皮肤站",
        _ => "离线账户"
    };

    public string Detail => Stored.Kind switch
    {
        AccountKind.Microsoft => "Minecraft 正版 · Xbox Live",
        AccountKind.Yggdrasil => HostOf(Stored.ServerUrl),
        _ => "本地模式 · 仅用于离线游戏"
    };

    public string Initial => string.IsNullOrWhiteSpace(Stored.Name)
        ? "?"
        : Stored.Name[..1].ToUpperInvariant();

    public string Uuid => Stored.Uuid;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private static string HostOf(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return "Yggdrasil";
        }

        return Uri.TryCreate(server, UriKind.Absolute, out var uri) ? uri.Host : server;
    }
}

public sealed class AccountsViewModel : ObservableObject
{
    private readonly PortablePaths _paths;
    private readonly LauncherContext _context;
    private readonly SettingsService _settings;
    private readonly AccountStore _store;
    private readonly Action<PageKey>? _navigate;
    private AccountItem? _selectedAccount;
    private string _offlineName = "Player";
    private string _yggdrasilServer = "https://littleskin.cn/api/yggdrasil";
    private string _yggdrasilUser = string.Empty;
    private string _yggdrasilPassword = string.Empty;
    private string _microsoftClientId = string.Empty;
    private string _deviceCode = string.Empty;
    private string _microsoftStatus = "尚未开始微软登录";
    private string _status = "账户数据保存在本机，访问令牌使用 Windows DPAPI 加密。";
    private bool _statusIsError;
    private bool _isBusy;
    private CancellationTokenSource? _microsoftCancellation;

    public AccountsViewModel(
        PortablePaths paths,
        LauncherContext context,
        SettingsService settings,
        Action<PageKey>? navigate = null)
    {
        _paths = paths;
        _context = context;
        _settings = settings;
        _navigate = navigate;
        _store = new AccountStore(paths);
        Accounts = [];
        AddOfflineCommand = new AsyncRelayCommand(AddOfflineAsync);
        LoginYggdrasilCommand = new AsyncRelayCommand(LoginYggdrasilAsync);
        StartMicrosoftCommand = new AsyncRelayCommand(StartMicrosoftAsync);
        CancelMicrosoftCommand = new RelayCommand(CancelMicrosoft);
        SelectAccountCommand = new AsyncRelayCommand(SelectAccountAsync);
        RemoveAccountCommand = new AsyncRelayCommand(RemoveAccountAsync);
        OpenMicrosoftPageCommand = new RelayCommand(OpenMicrosoftPage);
        _ = InitializeAsync();
    }

    public ObservableCollection<AccountItem> Accounts { get; }

    public AccountItem? SelectedAccount
    {
        get => _selectedAccount;
        private set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                foreach (var account in Accounts)
                {
                    account.IsSelected = ReferenceEquals(account, value);
                }

                OnPropertyChanged(nameof(CurrentAccount));
                OnPropertyChanged(nameof(CurrentAccountText));
                OnPropertyChanged(nameof(HasAccounts));
            }
        }
    }

    public GameAccount CurrentAccount => SelectedAccount?.Stored.ToAccount()
                                        ?? GameAccount.Offline("Player");

    /// <summary>
    /// Returns the account to pass to the game. Skin-site tokens are short
    /// lived and rotate after being bound to a character, so refresh and
    /// persist the new token before every launch.
    /// </summary>
    public async Task<GameAccount> GetLaunchAccountAsync(
        CancellationToken cancellationToken = default)
    {
        var item = SelectedAccount;
        var account = CurrentAccount;
        if (item is null || account.Kind != AccountKind.Yggdrasil)
        {
            return account;
        }

        try
        {
            var refreshed = await _context.Auth
                .RefreshYggdrasilAsync(account, cancellationToken)
                .ConfigureAwait(true);
            if (!string.Equals(
                    refreshed.AccessToken, account.AccessToken, StringComparison.Ordinal))
            {
                var stored = StoredAccount.FromAccount(refreshed);
                stored.Id = item.Stored.Id;
                stored.AddedAt = item.Stored.AddedAt;
                var replacement = new AccountItem(stored);
                var index = Accounts.IndexOf(item);
                if (index >= 0)
                {
                    Accounts[index] = replacement;
                }

                SelectedAccount = replacement;
                await PersistAsync(replacement.Id).ConfigureAwait(true);
            }

            return refreshed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error("刷新皮肤站令牌失败", ex);
            throw new InvalidOperationException(
                "皮肤站登录已失效，请到账户页面重新登录后再启动。", ex);
        }
    }

    public string CurrentAccountText => SelectedAccount is null
        ? "离线账户"
        : $"{SelectedAccount.Name} · {SelectedAccount.KindText}";

    public bool HasAccounts => Accounts.Count > 0;

    public string OfflineName
    {
        get => _offlineName;
        set => SetProperty(ref _offlineName, value);
    }

    public string YggdrasilServer
    {
        get => _yggdrasilServer;
        set => SetProperty(ref _yggdrasilServer, value);
    }

    public string YggdrasilUser
    {
        get => _yggdrasilUser;
        set => SetProperty(ref _yggdrasilUser, value);
    }

    public string YggdrasilPassword
    {
        get => _yggdrasilPassword;
        set => SetProperty(ref _yggdrasilPassword, value);
    }

    public string MicrosoftClientId
    {
        get => _microsoftClientId;
        set => SetProperty(ref _microsoftClientId, value);
    }

    public string DeviceCode
    {
        get => _deviceCode;
        private set => SetProperty(ref _deviceCode, value);
    }

    public bool HasDeviceCode => !string.IsNullOrWhiteSpace(DeviceCode);

    public string MicrosoftStatus
    {
        get => _microsoftStatus;
        private set => SetProperty(ref _microsoftStatus, value);
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

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public ICommand AddOfflineCommand { get; }

    public ICommand LoginYggdrasilCommand { get; }

    public ICommand StartMicrosoftCommand { get; }

    public ICommand CancelMicrosoftCommand { get; }

    public ICommand SelectAccountCommand { get; }

    public ICommand RemoveAccountCommand { get; }

    public ICommand OpenMicrosoftPageCommand { get; }

    public async Task InitializeAsync()
    {
        _microsoftClientId = _settings.Current.Microsoft.ClientId;
        OnPropertyChanged(nameof(MicrosoftClientId));

        var state = await _store.LoadAsync().ConfigureAwait(true);
        Accounts.Clear();
        foreach (var stored in state.Accounts)
        {
            Accounts.Add(new AccountItem(stored));
        }

        // Microsoft Minecraft tokens live for roughly a day. Refresh proactively
        // when expiry is near so the next launch does not fail with 401.
        var refreshedAny = false;
        if (!string.IsNullOrWhiteSpace(_microsoftClientId))
        {
            for (var index = 0; index < state.Accounts.Count; index++)
            {
                var stored = state.Accounts[index];
                if (stored.Kind != AccountKind.Microsoft
                    || string.IsNullOrEmpty(stored.ProtectedRefreshToken)
                    || (stored.AccessTokenExpiresAt is { } expiry
                        && expiry > DateTimeOffset.Now.AddMinutes(10)))
                {
                    continue;
                }

                try
                {
                    var account = await _context.Auth.RefreshMicrosoftAsync(
                            stored.ToAccount(),
                            _microsoftClientId)
                        .ConfigureAwait(true);
                    var updated = StoredAccount.FromAccount(account);
                    updated.Id = stored.Id;
                    updated.AddedAt = stored.AddedAt;
                    state.Accounts[index] = updated;
                    Accounts[index] = new AccountItem(updated);
                    refreshedAny = true;
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"微软令牌自动刷新失败：{ex.Message}");
                }
            }
        }

        if (refreshedAny)
        {
            await _store.SaveAsync(state).ConfigureAwait(true);
        }

        if (Accounts.Count == 0)
        {
            var offline = StoredAccount.FromAccount(GameAccount.Offline("Player"));
            Accounts.Add(new AccountItem(offline));
            state.SelectedId = offline.Id;
            state.Accounts.Add(offline);
            await _store.SaveAsync(state).ConfigureAwait(true);
        }

        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == state.SelectedId)
                          ?? Accounts.FirstOrDefault();
        OnPropertyChanged(nameof(HasAccounts));
    }

    private async Task AddOfflineAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(OfflineName) ? "Player" : OfflineName.Trim();
        if (Accounts.Any(a => a.Stored.Kind == AccountKind.Offline
                              && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusIsError = true;
            Status = $"已存在离线账户 {name}。";
            return;
        }

        var stored = StoredAccount.FromAccount(GameAccount.Offline(name));
        Accounts.Add(new AccountItem(stored));
        await PersistAsync(stored.Id).ConfigureAwait(true);
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == stored.Id);
        StatusIsError = false;
        Status = $"已添加离线账户 {name}，可直接开始游戏。";
    }

    private async Task LoginYggdrasilAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Status = "正在连接皮肤站…";
        try
        {
            var account = await _context.Auth.LoginYggdrasilAsync(
                    YggdrasilServer,
                    YggdrasilUser,
                    YggdrasilPassword,
                    ChooseCharacterAsync)
                .ConfigureAwait(true);
            YggdrasilPassword = string.Empty;
            var stored = StoredAccount.FromAccount(account);
            Accounts.Add(new AccountItem(stored));
            await PersistAsync(stored.Id).ConfigureAwait(true);
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == stored.Id);
            Status = $"已登录 {account.Name}（{new Uri(account.ServerUrl!).Host}）。";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task<YggdrasilCharacter?> ChooseCharacterAsync(
        IReadOnlyList<YggdrasilCharacter> characters)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult<YggdrasilCharacter?>(characters.FirstOrDefault());
        }

        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowCharacterDialog(characters));
        }

        return dispatcher.InvokeAsync(() => ShowCharacterDialog(characters)).Task;
    }

    private static YggdrasilCharacter? ShowCharacterDialog(
        IReadOnlyList<YggdrasilCharacter> characters)
    {
        var window = new CharacterSelectionWindow(characters);
        if (Application.Current?.MainWindow is { IsLoaded: true } owner)
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true ? window.SelectedCharacter : null;
    }

    private async Task StartMicrosoftAsync()
    {
        if (IsBusy)
        {
            return;
        }

        _microsoftCancellation?.Dispose();
        _microsoftCancellation = new CancellationTokenSource();
        IsBusy = true;
        StatusIsError = false;
        MicrosoftStatus = "正在打开微软登录页面…";
        Status = "等待浏览器完成登录…";
        try
        {
            _settings.Current.Microsoft.ClientId = MicrosoftClientId.Trim();
            await _settings.SaveAsync().ConfigureAwait(true);

            var account = await _context.Auth.LoginMicrosoftBrowserAsync(
                    MicrosoftClientId,
                    uri =>
                    {
                        MicrosoftStatus = "请在浏览器中完成登录，完成后会自动返回。";
                        OpenBrowser(uri.ToString());
                    },
                    _microsoftCancellation.Token)
                .ConfigureAwait(true);
            var stored = StoredAccount.FromAccount(account);
            Accounts.Add(new AccountItem(stored));
            await PersistAsync(stored.Id).ConfigureAwait(true);
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == stored.Id);
            Status = $"微软登录成功：{account.Name}";
            MicrosoftStatus = "已登录，可以开始正版游戏。";
        }
        catch (OperationCanceledException)
        {
            Status = "已取消微软登录。";
            MicrosoftStatus = "已取消";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            Status = ex.Message;
            MicrosoftStatus = "登录未完成";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelMicrosoft()
    {
        _microsoftCancellation?.Cancel();
    }

    private async Task SelectAccountAsync(object? parameter)
    {
        if (parameter is not AccountItem item)
        {
            return;
        }

        SelectedAccount = item;
        await PersistAsync(item.Id).ConfigureAwait(true);
    }

    private async Task RemoveAccountAsync(object? parameter)
    {
        if (parameter is not AccountItem item || IsBusy)
        {
            return;
        }

        Accounts.Remove(item);
        if (ReferenceEquals(SelectedAccount, item))
        {
            SelectedAccount = Accounts.FirstOrDefault();
        }

        await PersistAsync(SelectedAccount?.Id).ConfigureAwait(true);
        Status = $"已移除账户 {item.Name}。";
    }

    private async Task PersistAsync(string? selectedId)
    {
        var state = new AccountStoreState
        {
            Accounts = Accounts.Select(a => a.Stored).ToList(),
            SelectedId = selectedId
        };
        await _store.SaveAsync(state).ConfigureAwait(true);
    }

    private void OpenMicrosoftPage()
    {
        OpenBrowser("https://microsoft.com/link");
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // The code is still shown in the UI.
        }
    }
}
