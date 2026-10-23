using System.Text.Json;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task LoadUsesDefaultsWhenFileIsMissing()
    {
        using var fixture = new SettingsFixture();
        var service = new SettingsService(fixture.Paths);
        var settings = await service.LoadAsync();
        Assert.Equal(ThemeMode.System, settings.Appearance.ThemeMode);
        Assert.Equal("#4ADE80", settings.Appearance.AccentColor);
        Assert.Equal(0.72, settings.Appearance.Glass.Opacity, 3);
        Assert.True(double.IsNaN(settings.Window.Left));
    }

    [Fact]
    public async Task LoadClampsUnsafeValues()
    {
        using var fixture = new SettingsFixture();
        var json =
            """
            {
              "appearance": {
                "accentColor": "oops",
                "background": { "mode": "image", "path": "backgrounds/test.png", "dim": 5, "blur": 99 },
                "glass": { "opacity": 0, "blur": -3, "noise": 9 }
              },
              "downloads": { "concurrency": 999, "timeoutSeconds": 1 },
              "window": { "width": 10, "height": 9000, "left": "NaN", "top": "NaN" }
            }
            """;
        await File.WriteAllTextAsync(fixture.Paths.ConfigPath, json);
        var service = new SettingsService(fixture.Paths);
        var settings = await service.LoadAsync();

        Assert.Equal("#4ADE80", settings.Appearance.AccentColor);
        Assert.Equal(0.9, settings.Appearance.Background.Dim, 3);
        Assert.Equal(40, settings.Appearance.Background.Blur, 3);
        Assert.Equal(0, settings.Appearance.Glass.Opacity, 3);
        Assert.Equal(0, settings.Appearance.Glass.Blur, 3);
        Assert.Equal(0.25, settings.Appearance.Glass.Noise, 3);
        Assert.Equal(32, settings.Downloads.Concurrency);
        Assert.Equal(5, settings.Downloads.TimeoutSeconds);
        Assert.Equal(980, settings.Window.Width, 3);
        Assert.Equal(2160, settings.Window.Height, 3);
    }

    [Fact]
    public async Task MultipleRootsDeduplicatePathsAndNames()
    {
        using var fixture = new SettingsFixture();
        var json =
            """
            {
              "game": {
                "mountMode": "Multiple",
                "roots": [
                  { "path": "C:\\games\\one", "name": "主世界" },
                  { "path": "C:\\games\\one", "name": "重复路径" },
                  { "path": "C:\\games\\two", "name": "主世界" }
                ]
              }
            }
            """;
        await File.WriteAllTextAsync(fixture.Paths.ConfigPath, json);
        var settings = await new SettingsService(fixture.Paths).LoadAsync();

        Assert.Equal(GameRootMountMode.Multiple, settings.Game.MountMode);
        Assert.Equal(2, settings.Game.Roots.Count);
        Assert.Equal("主世界", settings.Game.Roots[0].Name);
        Assert.Equal("主世界-2", settings.Game.Roots[1].Name);
    }

    [Fact]
    public async Task SaveRoundTripsAndWritesBackup()
    {
        using var fixture = new SettingsFixture();
        var service = new SettingsService(fixture.Paths);
        await service.LoadAsync();
        service.Current.Appearance.ThemeMode = ThemeMode.Light;
        service.Current.Appearance.AccentColor = "#112233";
        service.Current.Java.DefaultRuntimeId = "java-runtime-gamma";
        await service.SaveAsync();
        await service.SaveAsync();

        var reloaded = await new SettingsService(fixture.Paths).LoadAsync();
        Assert.Equal(ThemeMode.Light, reloaded.Appearance.ThemeMode);
        Assert.Equal("#112233", reloaded.Appearance.AccentColor);
        Assert.Equal("java-runtime-gamma", reloaded.Java.DefaultRuntimeId);
        Assert.True(File.Exists(fixture.Paths.ConfigBackupPath));
    }

    [Fact]
    public async Task CorruptConfigFallsBackToBackup()
    {
        using var fixture = new SettingsFixture();
        var service = new SettingsService(fixture.Paths);
        await service.LoadAsync();
        service.Current.Appearance.AccentColor = "#ABCDEF";
        await service.SaveAsync();
        await service.SaveAsync();
        await File.WriteAllTextAsync(fixture.Paths.ConfigPath, "{ this is not json");

        var reloaded = await new SettingsService(fixture.Paths).LoadAsync();
        Assert.Equal("#ABCDEF", reloaded.Appearance.AccentColor);
    }

    [Fact]
    public async Task BackgroundModeWithoutPathFallsBackToNone()
    {
        using var fixture = new SettingsFixture();
        await File.WriteAllTextAsync(
            fixture.Paths.ConfigPath,
            """{"appearance":{"background":{"mode":"video","path":""}}}""");
        var settings = await new SettingsService(fixture.Paths).LoadAsync();
        Assert.Equal(BackgroundMode.None, settings.Appearance.Background.Mode);
        Assert.Null(settings.Appearance.Background.Path);
    }

    private sealed class SettingsFixture : IDisposable
    {
        public SettingsFixture()
        {
            var root = Path.Combine(Path.GetTempPath(), "qs-settings-" + Guid.NewGuid().ToString("N"));
            Paths = new PortablePaths(root);
            Paths.EnsureLayout();
        }

        public PortablePaths Paths { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Paths.Root))
                {
                    Directory.Delete(Paths.Root, true);
                }
            }
            catch
            {
                // Temporary test data; best-effort cleanup.
            }
        }
    }
}
