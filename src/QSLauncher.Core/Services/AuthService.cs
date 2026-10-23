using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed record YggdrasilCharacter(string Id, string Name);

public sealed record MicrosoftDeviceCode(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string Message,
    int IntervalSeconds,
    DateTimeOffset ExpiresAt);

public sealed class AuthService
{
    private const string DeviceCodeUrl =
        "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenUrl =
        "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XboxLiveUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginUrl =
        "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string MinecraftProfileUrl =
        "https://api.minecraftservices.com/minecraft/profile";

    private readonly HttpService _http;

    public AuthService(HttpService http)
    {
        _http = http;
    }

    public Task<GameAccount> LoginYggdrasilAsync(
        string server,
        string username,
        string password,
        CancellationToken cancellationToken = default)
        => LoginYggdrasilAsync(server, username, password, null, cancellationToken);

    public async Task<GameAccount> LoginYggdrasilAsync(
        string server,
        string username,
        string password,
        Func<IReadOnlyList<YggdrasilCharacter>, Task<YggdrasilCharacter?>>? chooseCharacter,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("请输入用户名和密码。");
        }

        var root = NormalizeServer(server);
        var request = new YggdrasilAuthenticateRequest
        {
            Username = username.Trim(),
            Password = password,
            ClientToken = Guid.NewGuid().ToString("N"),
            RequestUser = false
        };

        YggdrasilAuthenticateResponse response;
        try
        {
            response = await _http.PostJsonAsync<YggdrasilAuthenticateResponse>(
                    new Uri(root + "/authserver/authenticate"),
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "皮肤站登录失败：用户名/密码错误，或服务器拒绝了请求。", ex);
        }

        var characters = response.AvailableProfiles
            .Select(profile => new YggdrasilCharacter(profile.Id, profile.Name))
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Id))
            .ToList();
        if (response.SelectedProfile is { } selected)
        {
            var selectedCharacter = new YggdrasilCharacter(selected.Id, selected.Name);
            if (characters.All(item => !string.Equals(
                    item.Id, selectedCharacter.Id, StringComparison.OrdinalIgnoreCase)))
            {
                characters.Insert(0, selectedCharacter);
            }
        }

        if (characters.Count == 0)
        {
            throw new InvalidOperationException("该账号还没有创建 Minecraft 角色。");
        }

        if (string.IsNullOrWhiteSpace(response.AccessToken))
        {
            throw new InvalidOperationException("皮肤站没有返回访问令牌。");
        }

        YggdrasilCharacter profile;
        if (characters.Count > 1 && chooseCharacter is not null)
        {
            profile = await chooseCharacter(characters).ConfigureAwait(false)
                      ?? throw new OperationCanceledException("已取消选择角色。");
        }
        else
        {
            profile = response.SelectedProfile is { } initial
                ? characters.First(item => string.Equals(
                    item.Id, initial.Id, StringComparison.OrdinalIgnoreCase))
                : characters[0];
        }

        var accessToken = response.AccessToken!;
        var clientToken = response.ClientToken ?? request.ClientToken;
        var alreadyBound = response.SelectedProfile is { } selectedProfile
                           && string.Equals(
                               selectedProfile.Id,
                               profile.Id,
                               StringComparison.OrdinalIgnoreCase);
        if (!alreadyBound)
        {
            try
            {
                var bound = await SelectYggdrasilProfileAsync(
                        root,
                        accessToken,
                        clientToken,
                        profile.Id,
                        profile.Name,
                        cancellationToken)
                    .ConfigureAwait(false);
                accessToken = bound.AccessToken!;
                clientToken = bound.ClientToken ?? clientToken;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "皮肤站登录成功，但无法把访问令牌绑定到所选角色。请确认角色属于该账号后重试。", ex);
            }
        }

        return new GameAccount
        {
            Name = string.IsNullOrWhiteSpace(profile.Name) ? username.Trim() : profile.Name,
            Uuid = DashedUuid(profile.Id),
            AccessToken = accessToken,
            UserType = "mojang",
            ClientToken = clientToken,
            Kind = AccountKind.Yggdrasil,
            ServerUrl = root
        };
    }

    /// <summary>
    /// Binds the freshly issued Yggdrasil token to the character the player
    /// picked. Skin sites such as LittleSkin issue tokens without a
    /// <c>selectedProfile</c> claim until this refresh happens; the game then
    /// accepts the login locally but every authenticated server rejects it.
    /// Some implementations expect the profile as an object, older ones as a
    /// plain id string, so try both forms.
    /// </summary>
    private async Task<YggdrasilAuthenticateResponse> SelectYggdrasilProfileAsync(
        string root,
        string accessToken,
        string clientToken,
        string profileId,
        string profileName,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(root + "/authserver/refresh");
        var id = profileId.Replace("-", string.Empty, StringComparison.Ordinal);
        try
        {
            var response = await _http.PostJsonAsync<YggdrasilAuthenticateResponse>(
                    endpoint,
                    new
                    {
                        accessToken,
                        clientToken,
                        selectedProfile = new { id, name = profileName },
                        requestUser = false
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(response.AccessToken))
            {
                return response;
            }
        }
        catch (HttpRequestException)
        {
            // Fall back to the original Yggdrasil form below.
        }

        var legacyResponse = await _http.PostJsonAsync<YggdrasilAuthenticateResponse>(
                endpoint,
                new
                {
                    accessToken,
                    clientToken,
                    selectedProfile = id,
                    requestUser = false
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(legacyResponse.AccessToken))
        {
            throw new InvalidOperationException("皮肤站没有返回绑定角色后的访问令牌。");
        }

        return legacyResponse;
    }

    /// <summary>
    /// Opens the official Microsoft sign-in page in the system browser and
    /// completes the OAuth authorization-code + PKCE flow on a loopback
    /// listener. No code needs to be copied by the user.
    /// </summary>
    public async Task<GameAccount> LoginMicrosoftBrowserAsync(
        string clientId,
        Action<Uri> openBrowser,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "请先填写 Azure 应用（公共客户端）的应用 ID。");
        }

        ArgumentNullException.ThrowIfNull(openBrowser);

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://localhost:{port}/callback";
        var authorizeUri = new Uri(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId.Trim())}" +
            "&response_type=code&response_mode=query" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString("XboxLive.signin offline_access")}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            "&code_challenge_method=S256&prompt=select_account");

        openBrowser(authorizeUri);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        string code;
        try
        {
            code = await WaitForOAuthCodeAsync(listener, state, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("微软登录超时，请重新尝试。");
        }

        using var tokenDocument = await _http.PostFormDocumentAsync(
                new Uri(TokenUrl),
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = clientId.Trim(),
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["code_verifier"] = verifier
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (tokenDocument.RootElement.TryGetProperty("error", out var error))
        {
            var description = tokenDocument.RootElement.TryGetProperty(
                    "error_description", out var detail)
                ? detail.GetString()
                : error.GetString();
            throw new InvalidOperationException($"微软登录失败：{description}");
        }

        var accessToken = tokenDocument.RootElement.TryGetProperty(
                "access_token", out var accessElement)
            ? accessElement.GetString()
            : null;
        var refreshToken = tokenDocument.RootElement.TryGetProperty(
                "refresh_token", out var refreshElement)
            ? refreshElement.GetString()
            : null;
        var lifetime = tokenDocument.RootElement.TryGetProperty(
                "expires_in", out var lifetimeElement)
            ? Math.Max(60, lifetimeElement.GetInt32())
            : 86400;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("微软没有返回访问令牌，请重新登录。");
        }

        var account = await LoginWithXboxAsync(
                accessToken,
                refreshToken,
                DateTimeOffset.Now.AddSeconds(lifetime),
                cancellationToken)
            .ConfigureAwait(false);
        return new GameAccount
        {
            Name = account.Name,
            Uuid = account.Uuid,
            AccessToken = account.AccessToken,
            UserType = account.UserType,
            Xuid = account.Xuid,
            Kind = AccountKind.Microsoft,
            RefreshToken = account.RefreshToken,
            AccessTokenExpiresAt = account.AccessTokenExpiresAt,
            ClientId = clientId.Trim()
        };
    }

    public async Task<GameAccount> LoginMicrosoftAsync(
        string clientId,
        Action<MicrosoftDeviceCode>? onDeviceCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "请先在设置中填写你自己的 Azure 应用客户端 ID。启动器不会内置公共客户端 ID。");
        }

        var codeDocument = await _http.PostFormDocumentAsync(
                new Uri(DeviceCodeUrl),
                new Dictionary<string, string>
                {
                    ["client_id"] = clientId.Trim(),
                    ["scope"] = "XboxLive.signin offline_access"
                },
                cancellationToken)
            .ConfigureAwait(false);

        using (codeDocument)
        {
            if (codeDocument.RootElement.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(
                    $"微软设备代码请求失败：{error.GetString()}");
            }

            var deviceCode = codeDocument.RootElement.GetProperty("device_code").GetString()
                             ?? throw new InvalidOperationException("设备代码为空。");
            var userCode = codeDocument.RootElement.GetProperty("user_code").GetString()
                           ?? string.Empty;
            var verificationUri = codeDocument.RootElement
                                      .TryGetProperty("verification_uri", out var uri)
                ? uri.GetString() ?? "https://microsoft.com/link"
                : "https://microsoft.com/link";
            var message = codeDocument.RootElement
                              .TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? string.Empty
                : $"请打开 {verificationUri} 并输入代码 {userCode}";
            var interval = codeDocument.RootElement.TryGetProperty("interval", out var intervalElement)
                ? Math.Max(2, intervalElement.GetInt32())
                : 5;
            var expiresIn = codeDocument.RootElement.TryGetProperty("expires_in", out var expiresElement)
                ? Math.Max(30, expiresElement.GetInt32())
                : 900;

            onDeviceCode?.Invoke(new MicrosoftDeviceCode(
                deviceCode,
                userCode,
                verificationUri,
                message,
                interval,
                DateTimeOffset.Now.AddSeconds(expiresIn)));

            var deadline = DateTimeOffset.Now.AddSeconds(expiresIn);
            string? accessToken = null;
            string? refreshToken = null;
            var tokenLifetime = 86400;
            while (DateTimeOffset.Now < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken)
                    .ConfigureAwait(false);

                var tokenDocument = await _http.PostFormDocumentAsync(
                        new Uri(TokenUrl),
                        new Dictionary<string, string>
                        {
                            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                            ["client_id"] = clientId.Trim(),
                            ["device_code"] = deviceCode
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                using (tokenDocument)
                {
                    if (tokenDocument.RootElement.TryGetProperty("access_token", out var tokenElement))
                    {
                        accessToken = tokenElement.GetString();
                        refreshToken = tokenDocument.RootElement.TryGetProperty(
                                "refresh_token", out var refreshElement)
                            ? refreshElement.GetString()
                            : null;
                        tokenLifetime = tokenDocument.RootElement.TryGetProperty(
                                "expires_in", out var lifetimeElement)
                            ? Math.Max(60, lifetimeElement.GetInt32())
                            : tokenLifetime;
                        break;
                    }

                    var tokenError = tokenDocument.RootElement
                        .TryGetProperty("error", out var errorElement)
                        ? errorElement.GetString()
                        : null;
                    switch (tokenError)
                    {
                        case "authorization_pending":
                            continue;
                        case "slow_down":
                            interval += 5;
                            continue;
                        case "expired_token":
                            throw new InvalidOperationException("设备代码已过期，请重新登录。");
                        case "authorization_declined":
                            throw new InvalidOperationException("微软登录已被拒绝。");
                        default:
                            throw new InvalidOperationException(
                                $"微软登录失败：{tokenError ?? "未知错误"}");
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("微软登录超时，请重试。");
            }

            return await LoginWithXboxAsync(
                    accessToken,
                    refreshToken,
                    DateTimeOffset.Now.AddSeconds(tokenLifetime),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Uses the stored OAuth refresh token to obtain a fresh Minecraft access
    /// token. Called before launch when the cached token is close to expiry.
    /// </summary>
    public async Task<GameAccount> RefreshMicrosoftAsync(
        GameAccount account,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (account.Kind != AccountKind.Microsoft
            || string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            throw new InvalidOperationException("该账户没有可用的微软刷新令牌，请重新登录。");
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("缺少 Azure 客户端 ID，无法刷新微软登录。");
        }

        using var tokenDocument = await _http.PostFormDocumentAsync(
                new Uri(TokenUrl),
                new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = clientId.Trim(),
                    ["refresh_token"] = account.RefreshToken,
                    ["scope"] = "XboxLive.signin offline_access"
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (tokenDocument.RootElement.TryGetProperty("error", out var error))
        {
            var description = tokenDocument.RootElement.TryGetProperty(
                    "error_description", out var descriptionElement)
                ? descriptionElement.GetString()
                : null;
            throw new InvalidOperationException(
                $"微软登录已过期，请重新登录：{description ?? error.GetString()}");
        }

        var accessToken = tokenDocument.RootElement.TryGetProperty("access_token", out var tokenElement)
            ? tokenElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("微软没有返回新的访问令牌，请重新登录。");
        }

        var refreshToken = tokenDocument.RootElement.TryGetProperty(
                "refresh_token", out var refreshElement)
            ? refreshElement.GetString()
            : account.RefreshToken;
        var lifetime = tokenDocument.RootElement.TryGetProperty(
                "expires_in", out var lifetimeElement)
            ? Math.Max(60, lifetimeElement.GetInt32())
            : 86400;

        var refreshed = await LoginWithXboxAsync(
                accessToken,
                refreshToken,
                DateTimeOffset.Now.AddSeconds(lifetime),
                cancellationToken)
            .ConfigureAwait(false);
        return new GameAccount
        {
            Name = refreshed.Name,
            Uuid = refreshed.Uuid,
            AccessToken = refreshed.AccessToken,
            UserType = refreshed.UserType,
            Xuid = refreshed.Xuid,
            Kind = AccountKind.Microsoft,
            RefreshToken = refreshed.RefreshToken,
            AccessTokenExpiresAt = refreshed.AccessTokenExpiresAt,
            ClientId = clientId.Trim()
        };
    }

    public async Task<GameAccount> RefreshYggdrasilAsync(
        GameAccount account,
        CancellationToken cancellationToken = default)
    {
        if (account.Kind != AccountKind.Yggdrasil
            || string.IsNullOrWhiteSpace(account.ServerUrl)
            || string.IsNullOrWhiteSpace(account.ClientToken))
        {
            return account;
        }

        var refreshed = await SelectYggdrasilProfileAsync(
                account.ServerUrl.TrimEnd('/'),
                account.AccessToken,
                account.ClientToken,
                account.Uuid,
                account.Name,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(refreshed.AccessToken))
        {
            return account;
        }

        return new GameAccount
        {
            Name = account.Name,
            Uuid = account.Uuid,
            AccessToken = refreshed.AccessToken,
            UserType = account.UserType,
            Xuid = account.Xuid,
            ClientId = account.ClientId,
            Kind = account.Kind,
            ServerUrl = account.ServerUrl,
            ClientToken = refreshed.ClientToken ?? account.ClientToken
        };
    }

    private async Task<GameAccount> LoginWithXboxAsync(
        string microsoftAccessToken,
        string? refreshToken,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        var xbl = await _http.PostJsonAsync<XboxAuthResponse>(
                new Uri(XboxLiveUrl),
                new
                {
                    Properties = new
                    {
                        RpsTicket = "d=" + microsoftAccessToken,
                        SiteName = "user.auth.xboxlive.com"
                    },
                    RelyingParty = "http://auth.xboxlive.com",
                    TokenType = "JWT"
                },
                cancellationToken)
            .ConfigureAwait(false);

        var xsts = await _http.PostJsonAsync<XboxAuthResponse>(
                new Uri(XstsUrl),
                new
                {
                    Properties = new
                    {
                        SandboxId = "RETAIL",
                        UserTokens = new[] { xbl.Token }
                    },
                    RelyingParty = "rp://api.minecraftservices.com/",
                    TokenType = "JWT"
                },
                cancellationToken)
            .ConfigureAwait(false);

        var userHash = xsts.DisplayClaims?.Xui?.FirstOrDefault()?.UserHash;
        if (string.IsNullOrWhiteSpace(xsts.Token) || string.IsNullOrWhiteSpace(userHash))
        {
            throw new InvalidOperationException("Xbox 身份验证没有返回有效令牌。");
        }

        var minecraft = await _http.PostJsonAsync<MinecraftAuthResponse>(
                new Uri(MinecraftLoginUrl),
                new { identityToken = $"XBL3.0 x={userHash};{xsts.Token}" },
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(minecraft.AccessToken))
        {
            throw new InvalidOperationException("Minecraft 登录没有返回访问令牌。");
        }

        var profile = await _http.GetJsonAsync<MinecraftProfile>(
                new Uri(MinecraftProfileUrl),
                minecraft.AccessToken,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Id))
        {
            throw new InvalidOperationException("该微软账号没有 Minecraft Java 版档案。");
        }

        return new GameAccount
        {
            Name = profile.Name,
            Uuid = DashedUuid(profile.Id),
            AccessToken = minecraft.AccessToken,
            UserType = "msa",
            Xuid = profile.Xuid,
            Kind = AccountKind.Microsoft,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = expiresAt
        };
    }

    public static string NormalizeServer(string server)
    {
        var trimmed = string.IsNullOrWhiteSpace(server)
            ? "https://littleskin.cn/api/yggdrasil"
            : server.Trim().TrimEnd('/');
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        return trimmed;
    }

    private static async Task<string> WaitForOAuthCodeAsync(
        TcpListener listener,
        string expectedState,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);
            string? headerLine;
            while (!string.IsNullOrEmpty(
                       headerLine = await reader.ReadLineAsync(cancellationToken)
                           .ConfigureAwait(false)))
            {
                // Drain headers until the blank line.
            }

            var parts = requestLine?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts is not { Length: >= 2 })
            {
                await WriteHttpResponseAsync(
                        stream, 400, "登录回调格式无效。", cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var target = parts[1];
            if (!target.StartsWith("/callback", StringComparison.OrdinalIgnoreCase))
            {
                await WriteHttpResponseAsync(
                        stream, 404, "没有正在等待的登录请求。", cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var queryIndex = target.IndexOf('?');
            var query = queryIndex >= 0 ? target[(queryIndex + 1)..] : string.Empty;
            var values = ParseQuery(query);
            if (values.TryGetValue("error", out var error))
            {
                var description = values.TryGetValue("error_description", out var detail)
                    ? detail
                    : error;
                await WriteHttpResponseAsync(
                        stream, 400, $"微软登录未完成：{description}", cancellationToken)
                    .ConfigureAwait(false);
                throw new InvalidOperationException($"微软登录未完成：{description}");
            }

            if (!values.TryGetValue("state", out var actualState)
                || !string.Equals(actualState, expectedState, StringComparison.Ordinal))
            {
                await WriteHttpResponseAsync(
                        stream, 400, "登录状态校验失败，请回到启动器重试。", cancellationToken)
                    .ConfigureAwait(false);
                throw new InvalidOperationException("微软登录状态校验失败，请重试。");
            }

            if (!values.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            {
                await WriteHttpResponseAsync(
                        stream, 400, "微软没有返回授权码，请重试。", cancellationToken)
                    .ConfigureAwait(false);
                throw new InvalidOperationException("微软没有返回授权码，请重试。");
            }

            await WriteHttpResponseAsync(
                    stream, 200,
                    "登录完成，可以关闭此页面并返回 QS Launcher。",
                    cancellationToken)
                .ConfigureAwait(false);
            return code;
        }
    }

    private static async Task WriteHttpResponseAsync(
        Stream stream,
        int status,
        string message,
        CancellationToken cancellationToken)
    {
        var html = $$"""
            <!doctype html>
            <html lang="zh-CN"><head><meta charset="utf-8">
            <title>QS Launcher</title>
            <style>
            body{font-family:Segoe UI,Microsoft YaHei,sans-serif;background:#0f172a;color:#e2e8f0;
            display:flex;min-height:100vh;align-items:center;justify-content:center;margin:0}
            main{max-width:520px;padding:40px;text-align:center}
            p{color:#94a3b8;line-height:1.7}
            </style></head><body><main><h2>QS Launcher</h2>
            <p>{{WebUtility.HtmlEncode(message)}}</p></main></body></html>
            """;
        var body = Encoding.UTF8.GetBytes(html);
        var reason = status == 200 ? "OK" : "Bad Request";
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(part[..separator]);
            var value = Uri.UnescapeDataString(part[(separator + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string DashedUuid(string value)
    {
        var hex = new string(value.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        if (hex.Length != 32)
        {
            return value;
        }

        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    private sealed class YggdrasilAuthenticateRequest
    {
        [JsonPropertyName("agent")]
        public object Agent { get; set; } = new { name = "Minecraft", version = 1 };

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("clientToken")]
        public string? ClientToken { get; set; }

        [JsonPropertyName("requestUser")]
        public bool RequestUser { get; set; }
    }

    private sealed class YggdrasilAuthenticateResponse
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("clientToken")]
        public string? ClientToken { get; set; }

        [JsonPropertyName("selectedProfile")]
        public YggdrasilProfile? SelectedProfile { get; set; }

        [JsonPropertyName("availableProfiles")]
        public List<YggdrasilProfile> AvailableProfiles { get; set; } = [];
    }

    private sealed class YggdrasilProfile
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class XboxAuthResponse
    {
        [JsonPropertyName("Token")]
        public string? Token { get; set; }

        [JsonPropertyName("DisplayClaims")]
        public XboxDisplayClaims? DisplayClaims { get; set; }
    }

    private sealed class XboxDisplayClaims
    {
        [JsonPropertyName("xui")]
        public List<XboxUserClaim>? Xui { get; set; }
    }

    private sealed class XboxUserClaim
    {
        [JsonPropertyName("uhs")]
        public string? UserHash { get; set; }
    }

    private sealed class MinecraftAuthResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private sealed class MinecraftProfile
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("xuid")]
        public string? Xuid { get; set; }
    }
}
