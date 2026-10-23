using System.Text.Json;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

/// <summary>
/// Downloads authlib-injector from its official distribution endpoint and
/// verifies the published SHA-256 before it is attached to a third-party
/// Yggdrasil launch.
/// </summary>
public sealed class AuthlibInjectorService
{
    private const string MetadataUrl =
        "https://authlib-injector.yushi.moe/artifact/latest.json";

    private readonly PortablePaths _paths;
    private readonly HttpService _http;

    public AuthlibInjectorService(PortablePaths paths, HttpService http)
    {
        _paths = paths;
        _http = http;
    }

    public string JarPath => Path.Combine(_paths.AuthlibPath, "authlib-injector.jar");

    public async Task<string> EnsureAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var metadata = await _http.GetJsonDocumentAsync(
                new Uri(MetadataUrl), cancellationToken)
            .ConfigureAwait(false);
        var root = metadata.RootElement;
        var downloadUrl = root.TryGetProperty("download_url", out var urlElement)
            ? urlElement.GetString()
            : null;
        var sha256 = root.TryGetProperty("checksums", out var checksums)
                     && checksums.TryGetProperty("sha256", out var hashElement)
            ? hashElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(sha256))
        {
            throw new InvalidOperationException("authlib-injector 元数据缺少下载地址或校验值。");
        }

        Directory.CreateDirectory(_paths.AuthlibPath);
        await _http.DownloadFileAsync(
                new Uri(downloadUrl),
                JarPath,
                sha256: sha256,
                progress: progress,
                label: "authlib-injector",
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        AppLog.Info($"authlib-injector 已就绪：{JarPath}");
        return JarPath;
    }

    public static string BuildJavaAgentArgument(string jarPath, string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(jarPath)
            || string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new ArgumentException("authlib-injector 路径与服务器地址不能为空。");
        }

        return $"-javaagent:{jarPath}={serverUrl.TrimEnd('/')}";
    }
}
