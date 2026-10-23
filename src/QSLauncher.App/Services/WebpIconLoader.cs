using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using QSLauncher.Core.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace QSLauncher.App.Services;

/// <summary>
/// Modrinth serves project icons as WebP. WPF's image pipeline has no WebP
/// decoder on every Windows install, so icons are fetched once, converted to a
/// frozen PNG-backed bitmap, and reused by both mod and modpack result lists.
/// </summary>
public static class WebpIconLoader
{
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();

    public static async Task<BitmapImage?> LoadAsync(
        HttpService http,
        string? url,
        int decodeWidth,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var key = $"{decodeWidth}|{uri.AbsoluteUri}";
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bytes = await http.GetBytesAsync(uri, cancellationToken).ConfigureAwait(false);
        var bitmap = await Task.Run(() => Decode(bytes, decodeWidth), cancellationToken)
            .ConfigureAwait(false);
        Cache[key] = bitmap;
        return bitmap;
    }

    private static BitmapImage Decode(byte[] bytes, int decodeWidth)
    {
        using var source = Image.Load(bytes);
        using var png = new MemoryStream();
        source.SaveAsPng(png);
        png.Position = 0;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.StreamSource = png;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
