using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed class HttpService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly HttpClient _client;
    private bool _disposed;

    public HttpService(string? userAgent = null)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            AllowAutoRedirect = true
        };

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            userAgent ?? $"QSLauncher/{AppInfo.Version} (portable)");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
    }

    public async Task<string> GetStringAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> GetBytesAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> GetJsonAsync<T>(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        var text = await GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(text, JsonOptions)
               ?? throw new InvalidDataException($"服务器返回了空的 JSON：{uri}");
    }

    public async Task<T> PostJsonAsync<T>(
        Uri uri,
        object body,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            },
            cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(text, JsonOptions)
               ?? throw new InvalidDataException($"服务器返回了空的 JSON：{uri}");
    }

    public async Task<JsonDocument> PostFormDocumentAsync(
        Uri uri,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(text);
    }

    public async Task<T> GetJsonAsync<T>(
        Uri uri,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"服务器返回 HTTP {(int)response.StatusCode}。");
        }

        return JsonSerializer.Deserialize<T>(text, JsonOptions)
               ?? throw new InvalidDataException($"服务器返回了空的 JSON：{uri}");
    }

    public async Task<JsonDocument> GetJsonDocumentAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        var bytes = await GetBytesAsync(uri, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(bytes);
    }

    public async Task<bool> DownloadFileAsync(
        Uri uri,
        string destination,
        string? sha1 = null,
        string? sha256 = null,
        long expectedSize = 0,
        IProgress<DownloadProgress>? progress = null,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        if (await HashUtil.MatchesAsync(destination, sha1, sha256, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var display = label ?? Path.GetFileName(destination);

        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var temporary = destination + $".part-{Guid.NewGuid():N}";
            try
            {
                using var response = await SendWithRetryAsync(
                    () => new HttpRequestMessage(HttpMethod.Get, uri),
                    cancellationToken,
                    retries: 1).ConfigureAwait(false);

                var total = response.Content.Headers.ContentLength
                            ?? (expectedSize > 0 ? expectedSize : 0);
                await using (var input = await response.Content
                                 .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = new FileStream(
                                 temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                                 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[128 * 1024];
                    long received = 0;
                    var lastReport = DateTime.UtcNow;
                    progress?.Report(new DownloadProgress("download", display, 0, total));

                    while (true)
                    {
                        var read = await ReadWithTimeoutAsync(input, buffer, cancellationToken)
                            .ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                            .ConfigureAwait(false);
                        received += read;

                        if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 150)
                        {
                            progress?.Report(new DownloadProgress("download", display, received, total));
                            lastReport = DateTime.UtcNow;
                        }
                    }

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    progress?.Report(new DownloadProgress("download", display, received, total));
                }

                if (!await HashUtil.MatchesAsync(temporary, sha1, sha256, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new InvalidDataException($"文件校验失败：{display}");
                }

                File.Move(temporary, destination, overwrite: true);
                return true;
            }
            catch (OperationCanceledException)
            {
                SafeDelete(temporary);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            {
                SafeDelete(temporary);
                lastError = ex;
                AppLog.Warn($"下载失败（{attempt + 1}/3）：{uri} · {ex.Message}");
                if (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(700 * (attempt + 1)), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        throw new IOException($"下载失败：{display}", lastError);
    }

    public static Uri Combine(string baseUrl, string relative)
    {
        var normalizedBase = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        return new Uri(new Uri(normalizedBase), relative);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken,
        int retries = 3)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await _client.SendAsync(
                    requestFactory(),
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                var status = (int)response.StatusCode;
                response.Dispose();
                if (status is 408 or 429 or >= 500 && attempt < retries - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                throw new HttpRequestException($"服务器返回 HTTP {status}。");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
                if (attempt < retries - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        throw new HttpRequestException("网络请求失败，请检查连接或代理设置。", lastError);
    }

    private static async Task<int> ReadWithTimeoutAsync(
        Stream input,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            return await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("下载连接超时。");
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A stale partial file is harmless and will be ignored next time.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }
}
