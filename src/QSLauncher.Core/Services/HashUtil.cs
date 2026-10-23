using System.Security.Cryptography;

namespace QSLauncher.Core.Services;

public static class HashUtil
{
    public static async Task<string> Sha1Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<bool> MatchesAsync(
        string path,
        string? sha1,
        string? sha256 = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sha1))
        {
            var actual = await Sha1Async(path, cancellationToken).ConfigureAwait(false);
            return actual.Equals(sha1, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(sha256))
        {
            var actual = await Sha256Async(path, cancellationToken).ConfigureAwait(false);
            return actual.Equals(sha256, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}
