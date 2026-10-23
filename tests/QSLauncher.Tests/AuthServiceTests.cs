using System.Net;
using System.Net.Sockets;
using System.Text;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public void NormalizeServerAddsSchemeAndTrimsSlash()
    {
        Assert.Equal(
            "https://littleskin.cn/api/yggdrasil",
            AuthService.NormalizeServer("https://littleskin.cn/api/yggdrasil/"));
        Assert.Equal(
            "https://example.com/api/yggdrasil",
            AuthService.NormalizeServer("example.com/api/yggdrasil"));
        Assert.Equal(
            "https://littleskin.cn/api/yggdrasil",
            AuthService.NormalizeServer(""));
    }

    [Fact]
    public async Task YggdrasilLoginMapsProfileAndSendsExpectedRequest()
    {
        const string response = """
            {
              "accessToken": "token-123",
              "clientToken": "client-456",
              "selectedProfile": { "id": "0123456789abcdef0123456789abcdef", "name": "Steve" },
              "availableProfiles": []
            }
            """;
        using var server = new SingleRequestServer(response);
        using var http = new HttpService();
        var auth = new AuthService(http);

        var account = await auth.LoginYggdrasilAsync(
            server.BaseUrl, "steve@example.com", "secret");

        Assert.Equal("Steve", account.Name);
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", account.Uuid);
        Assert.Equal("token-123", account.AccessToken);
        Assert.Equal("client-456", account.ClientToken);
        Assert.Equal(AccountKind.Yggdrasil, account.Kind);
        Assert.Equal("mojang", account.UserType);
        Assert.Equal(server.BaseUrl, account.ServerUrl);

        var request = await server.Request;
        Assert.Equal("POST", request.Method);
        Assert.Equal("/authserver/authenticate", request.Path);
        Assert.Contains("\"username\":\"steve@example.com\"", request.Body);
        Assert.Contains("\"password\":\"secret\"", request.Body);
        Assert.DoesNotContain("steve@example.com", response);
    }

    [Fact]
    public async Task YggdrasilLoginLetsUserChooseAmongMultipleCharacters()
    {
        const string response = """
            {
              "accessToken": "token-123",
              "clientToken": "client-456",
              "selectedProfile": { "id": "11111111111111111111111111111111", "name": "First" },
              "availableProfiles": [
                { "id": "11111111111111111111111111111111", "name": "First" },
                { "id": "22222222222222222222222222222222", "name": "Second" }
              ]
            }
            """;
        using var server = new SingleRequestServer(response);
        using var http = new HttpService();
        var auth = new AuthService(http);
        IReadOnlyList<YggdrasilCharacter>? offered = null;

        var account = await auth.LoginYggdrasilAsync(
            server.BaseUrl, "user", "pass",
            characters =>
            {
                offered = characters;
                return Task.FromResult<YggdrasilCharacter?>(characters[1]);
            });

        Assert.NotNull(offered);
        Assert.Equal(2, offered!.Count);
        Assert.Equal("Second", account.Name);
        Assert.Equal("22222222-2222-2222-2222-222222222222", account.Uuid);
    }

    [Fact]
    public async Task YggdrasilRefreshReturnsUpdatedAccessToken()
    {
        const string response = """
            { "accessToken": "fresh-token", "clientToken": "client-456" }
            """;
        using var server = new SingleRequestServer(response);
        using var http = new HttpService();
        var auth = new AuthService(http);
        var account = new GameAccount
        {
            Name = "Steve",
            Uuid = "01234567-89ab-cdef-0123-456789abcdef",
            AccessToken = "old-token",
            ClientToken = "client-456",
            Kind = AccountKind.Yggdrasil,
            ServerUrl = server.BaseUrl,
            UserType = "mojang"
        };

        var refreshed = await auth.RefreshYggdrasilAsync(account);

        Assert.Equal("fresh-token", refreshed.AccessToken);
        Assert.Equal("client-456", refreshed.ClientToken);
        Assert.Equal("Steve", refreshed.Name);
        var request = await server.Request;
        Assert.Equal("/authserver/refresh", request.Path);
        Assert.Contains("\"accessToken\":\"old-token\"", request.Body);
    }

    [Fact]
    public async Task YggdrasilLoginBindsUnselectedTokenToCharacter()
    {
        const string authenticate = """
            {
              "accessToken": "unbound-token",
              "clientToken": "client-456",
              "selectedProfile": null,
              "availableProfiles": [
                { "id": "0123456789abcdef0123456789abcdef", "name": "Steve" }
              ]
            }
            """;
        const string refresh = """
            {
              "accessToken": "bound-token",
              "clientToken": "client-456",
              "selectedProfile": {
                "id": "0123456789abcdef0123456789abcdef",
                "name": "Steve"
              }
            }
            """;
        using var server = new SingleRequestServer(authenticate, refresh);
        using var http = new HttpService();
        var auth = new AuthService(http);

        var account = await auth.LoginYggdrasilAsync(
            server.BaseUrl, "steve@example.com", "secret");

        Assert.Equal("bound-token", account.AccessToken);
        Assert.Equal("Steve", account.Name);
        var request = await server.RefreshRequest;
        Assert.Equal("/authserver/refresh", request.Path);
        Assert.Contains(
            "\"selectedProfile\":{\"id\":\"0123456789abcdef0123456789abcdef\",\"name\":\"Steve\"}",
            request.Body);
    }

    [Fact]
    public void AuthlibAgentArgumentJoinsJarAndServer()
    {
        var argument = AuthlibInjectorService.BuildJavaAgentArgument(
            @"C:\QS\data\meta\authlib-injector\authlib-injector.jar",
            "https://littleskin.cn/api/yggdrasil/");

        Assert.Equal(
            @"-javaagent:C:\QS\data\meta\authlib-injector\authlib-injector.jar=https://littleskin.cn/api/yggdrasil",
            argument);
        Assert.Throws<ArgumentException>(
            () => AuthlibInjectorService.BuildJavaAgentArgument("", "https://x"));
    }

    [Fact]
    public async Task YggdrasilLoginWithoutProfileFailsClearly()
    {
        const string response = """
            { "accessToken": "token", "clientToken": "client", "availableProfiles": [] }
            """;
        using var server = new SingleRequestServer(response);
        using var http = new HttpService();
        var auth = new AuthService(http);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => auth.LoginYggdrasilAsync(server.BaseUrl, "user", "pass"));

        Assert.Contains("角色", error.Message);
    }

    private sealed record CapturedRequest(string Method, string Path, string Body);

    /// <summary>
    /// Minimal one-shot HTTP server used to test Yggdrasil request/response
    /// mapping without external services or credentials.
    /// </summary>
    private sealed class SingleRequestServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly TaskCompletionSource<CapturedRequest> _request =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<CapturedRequest> _refreshRequest =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _cts = new();
        private readonly string _responseBody;
        private readonly string? _refreshResponseBody;

        public SingleRequestServer(string responseBody, string? refreshResponseBody = null)
        {
            _responseBody = responseBody;
            _refreshResponseBody = refreshResponseBody;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUrl = $"http://127.0.0.1:{endpoint.Port}";
            _ = ServeAsync();
        }

        public string BaseUrl { get; }

        public Task<CapturedRequest> Request => _request.Task;

        public Task<CapturedRequest> RefreshRequest => _refreshRequest.Task;

        private async Task ServeAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(
                    stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(_cts.Token) ?? string.Empty;
                var parts = requestLine.Split(' ');
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_cts.Token)))
                {
                    var colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                    }
                }

                var length = headers.TryGetValue("Content-Length", out var rawLength)
                             && int.TryParse(rawLength, out var parsed)
                    ? parsed
                    : 0;
                var buffer = new char[length];
                var read = 0;
                while (read < length)
                {
                    var count = await reader.ReadAsync(buffer.AsMemory(read, length - read), _cts.Token);
                    if (count == 0)
                    {
                        break;
                    }

                    read += count;
                }

                var captured = new CapturedRequest(
                    parts.ElementAtOrDefault(0) ?? string.Empty,
                    parts.ElementAtOrDefault(1) ?? string.Empty,
                    new string(buffer, 0, read));
                _request.TrySetResult(captured);
                if (captured.Path == "/authserver/refresh")
                {
                    _refreshRequest.TrySetResult(captured);
                }

                var bodyBytes = Encoding.UTF8.GetBytes(
                    captured.Path == "/authserver/refresh"
                        ? _refreshResponseBody ?? _responseBody
                        : _responseBody);
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: application/json; charset=utf-8\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(header, _cts.Token);
                await stream.WriteAsync(bodyBytes, _cts.Token);
                await stream.FlushAsync(_cts.Token);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
            {
                _request.TrySetException(ex);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
