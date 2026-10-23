using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.Tests;

public sealed class GameRootTests
{
    [Fact]
    public async Task ScanFindsVersionsInsideDotMinecraftFolder()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"qslauncher-root-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, ".minecraft");
        var version = Path.Combine(root, "versions", "1.20.1");
        Directory.CreateDirectory(version);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(version, "1.20.1.json"),
                """
                {"id":"1.20.1","type":"release","mainClass":"net.minecraft.client.main.Main",
                 "assets":"5","libraries":[],"releaseTime":"2023-06-12T13:25:51+00:00"}
                """);
            await File.WriteAllBytesAsync(Path.Combine(version, "1.20.1.jar"), []);

            var service = new GameRootService();
            Assert.Equal(Path.GetFullPath(root), service.Normalize(parent));

            var versions = await service.ScanAsync(parent);
            var item = Assert.Single(versions);
            Assert.Equal("1.20.1", item.Id);
            Assert.Equal(LoaderKind.Vanilla, item.Loader);
            Assert.True(item.HasClientJar);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task ScanDetectsForgeFromMetadataWhenVersionNameIsCustom()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"qslauncher-root-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, ".minecraft");
        var version = Path.Combine(root, "versions", "ExamplePack1.6.7.1");
        Directory.CreateDirectory(version);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(version, "ExamplePack1.6.7.1.json"),
                """
                {
                  "id": "ExamplePack1.6.7.1",
                  "type": "release",
                  "clientVersion": "1.20.1",
                  "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
                  "libraries": [
                    {"name": "net.minecraftforge:accesstransformers:8.0.4"},
                    {"name": "net.minecraftforge:forgespi:7.0.1"}
                  ]
                }
                """);

            var service = new GameRootService();
            var item = Assert.Single(await service.ScanAsync(root));

            Assert.Equal(LoaderKind.Forge, item.Loader);
            Assert.Equal("1.20.1", item.MinecraftVersion);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingLinkedManifestIsRefreshedWithDetectedLoader()
    {
        var root = Path.Combine(Path.GetTempPath(), $"qslauncher-linked-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, ".minecraft");
        var version = Path.Combine(sourceRoot, "versions", "My Forge Pack");
        Directory.CreateDirectory(version);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(version, "My Forge Pack.json"),
                """
                {
                  "id": "My Forge Pack",
                  "type": "release",
                  "clientVersion": "1.20.1",
                  "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
                  "libraries": [{"name": "net.minecraftforge:forgespi:7.0.1"}]
                }
                """);

            var paths = new PortablePaths(root);
            paths.EnsureLayout();
            var service = new InstanceService(paths);
            var instance = await service.CreateAsync(
                "My Forge Pack", "My Forge Pack", "My Forge Pack");
            instance.Manifest.SourceRoot = sourceRoot;
            instance.Manifest.SourceVersionId = "My Forge Pack";
            await service.SaveAsync(instance);

            var refreshed = Assert.Single(await service.ListAsync());

            Assert.Equal(LoaderKind.Forge, refreshed.Manifest.Loader);
            Assert.Equal("1.20.1", refreshed.Manifest.MinecraftVersion);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
