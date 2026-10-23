using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.Services;

public sealed class StoredAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public AccountKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Uuid { get; set; } = string.Empty;

    public string ProtectedAccessToken { get; set; } = string.Empty;

    public string ProtectedRefreshToken { get; set; } = string.Empty;

    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    public string UserType { get; set; } = "legacy";

    public string? Xuid { get; set; }

    public string? ClientId { get; set; }

    public string? ServerUrl { get; set; }

    public string? ClientToken { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;

    public GameAccount ToAccount()
    {
        return new GameAccount
        {
            Name = Name,
            Uuid = Uuid,
            AccessToken = AccountStore.Unprotect(ProtectedAccessToken),
            UserType = UserType,
            Xuid = Xuid,
            ClientId = ClientId,
            Kind = Kind,
            ServerUrl = ServerUrl,
            ClientToken = ClientToken,
            RefreshToken = string.IsNullOrEmpty(ProtectedRefreshToken)
                ? null
                : AccountStore.Unprotect(ProtectedRefreshToken),
            AccessTokenExpiresAt = AccessTokenExpiresAt
        };
    }

    public static StoredAccount FromAccount(GameAccount account)
    {
        return new StoredAccount
        {
            Kind = account.Kind,
            Name = account.Name,
            Uuid = account.Uuid,
            ProtectedAccessToken = AccountStore.Protect(account.AccessToken),
            ProtectedRefreshToken = string.IsNullOrEmpty(account.RefreshToken)
                ? string.Empty
                : AccountStore.Protect(account.RefreshToken),
            AccessTokenExpiresAt = account.AccessTokenExpiresAt,
            UserType = account.UserType,
            Xuid = account.Xuid,
            ClientId = account.ClientId,
            ServerUrl = account.ServerUrl,
            ClientToken = account.ClientToken
        };
    }
}

public sealed class AccountStoreState
{
    public List<StoredAccount> Accounts { get; set; } = [];

    public string? SelectedId { get; set; }
}

public sealed class AccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly PortablePaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AccountStore(PortablePaths paths)
    {
        _paths = paths;
    }

    private string FilePath => Path.Combine(_paths.DataRoot, "accounts.json");

    public async Task<AccountStoreState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new AccountStoreState();
        }

        try
        {
            var json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AccountStoreState>(json, JsonOptions)
                   ?? new AccountStoreState();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"读取账户文件失败：{ex.Message}");
            return new AccountStoreState();
        }
    }

    public async Task SaveAsync(AccountStoreState state, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_paths.DataRoot);
            var temporary = FilePath + ".tmp";
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("保存账户文件失败", ex);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DPAPI 加密失败：{ex.Message}");
            return string.Empty;
        }
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0";
        }

        try
        {
            var bytes = Convert.FromBase64String(value);
            var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DPAPI 解密失败：{ex.Message}");
            return "0";
        }
    }
}
