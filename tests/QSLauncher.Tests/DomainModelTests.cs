using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.Tests;

public sealed class DomainModelTests
{
    [Fact]
    public void OfflineAccountUuidIsDeterministicAndDashed()
    {
        var first = GameAccount.Offline("Steve");
        var second = GameAccount.Offline("Steve");
        Assert.Equal(first.Uuid, second.Uuid);
        Assert.Equal("Steve", first.Name);
        Assert.Matches(
            "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            first.Uuid);

        var expected = Convert.ToHexString(
                MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:Steve")))
            .ToLowerInvariant();
        Assert.Equal($"{expected[..8]}-{expected[8..12]}-{expected[12..16]}-{expected[16..20]}-{expected[20..]}",
            first.Uuid);
    }

    [Fact]
    public void OfflineAccountFallsBackToPlayer()
    {
        Assert.Equal("Player", GameAccount.Offline(" ").Name);
        Assert.NotEqual(GameAccount.Offline("Alex").Uuid, GameAccount.Offline("Steve").Uuid);
    }

    [Theory]
    [InlineData("2026-10-20T04:27:37+0000", "2026-10-20T04:27:37+00:00")]
    [InlineData("2026-10-20T04:27:37Z", "2026-10-20T04:27:37+00:00")]
    [InlineData("2026-10-20T04:27:37+08:00", "2026-10-20T04:27:37+08:00")]
    public void FlexibleDateConverterAcceptsOffsetsWithoutColon(string input, string expected)
    {
        var value = JsonSerializer.Deserialize<VersionSummary>(
            $$"""{"id":"test","type":"release","time":"{{input}}","releaseTime":"{{input}}","url":"https://example.test"}""");
        Assert.NotNull(value);
        Assert.Equal(
            DateTimeOffset.Parse(expected),
            value.Time);
    }

    [Fact]
    public void FlexibleDateConverterRejectsGarbageAsNull()
    {
        var value = JsonSerializer.Deserialize<VersionSummary>(
            """{"id":"test","type":"release","time":"not-a-date","releaseTime":"not-a-date","url":"https://example.test"}""");
        Assert.NotNull(value);
        Assert.Null(value.Time);
    }

    [Fact]
    public void PortablePathsResolveOnlyInsideDataDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "qs-paths-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new PortablePaths(root);
            Assert.Equal(Path.Combine(root, "data"), paths.DataRoot);
            Assert.Equal(
                Path.Combine(paths.DataRoot, "instances", "demo"),
                paths.ResolveDataPath("instances/demo"));
            Assert.Equal(Path.Combine(paths.DataRoot, "config.json"),
                paths.ResolveDataPath("instances/../config.json"));
            Assert.Throws<InvalidOperationException>(
                () => paths.ResolveDataPath("../config.json"));
            Assert.Throws<InvalidOperationException>(
                () => paths.ResolveDataPath("../../outside.txt"));
            Assert.Throws<InvalidOperationException>(
                () => paths.ResolveDataPath(Path.Combine(Path.GetTempPath(), "absolute.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void PortablePathsImportsSupportedMediaWithUniqueNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "qs-media-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(Path.GetTempPath(), "qs-background-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var paths = new PortablePaths(root);
            paths.EnsureLayout();
            File.WriteAllBytes(source, [0x89, 0x50, 0x4E, 0x47]);

            var first = paths.ImportBackground(source, BackgroundMode.Image);
            var second = paths.ImportBackground(source, BackgroundMode.Image);
            Assert.True(File.Exists(paths.ResolveDataPath(first)));
            Assert.True(File.Exists(paths.ResolveDataPath(second)));
            Assert.NotEqual(first, second);
            Assert.StartsWith("backgrounds/", first.Replace('\\', '/'));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            if (File.Exists(source))
            {
                File.Delete(source);
            }
        }
    }

    [Fact]
    public void PortablePathsRejectsUnknownBackgroundExtension()
    {
        var root = Path.Combine(Path.GetTempPath(), "qs-media-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(Path.GetTempPath(), "qs-background-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllBytes(source, [0]);
            var paths = new PortablePaths(root);
            paths.EnsureLayout();
            Assert.Throws<InvalidOperationException>(
                () => paths.ImportBackground(source, BackgroundMode.Image));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            if (File.Exists(source))
            {
                File.Delete(source);
            }
        }
    }
}
