using System.IO.Compression;
using System.Text;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.Tests;

public sealed class InstanceContentTests
{
    [Fact]
    public async Task ListsResourcePacksAndReadsPackFormat()
    {
        using var fixture = new ContentFixture();
        var directory = fixture.Service.GetDirectory(fixture.Instance, ContentKind.ResourcePacks);
        Directory.CreateDirectory(directory);
        WriteZip(
            Path.Combine(directory, "demo.zip"),
            ("pack.mcmeta", """{"pack":{"pack_format":34,"description":"Demo"}}"""));
        var folder = Path.Combine(directory, "loose-pack");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "pack.mcmeta"), """{"pack":{"pack_format":15}}""");

        var items = await fixture.Service.ListAsync(fixture.Instance, ContentKind.ResourcePacks);

        Assert.Equal(2, items.Count);
        var zip = Assert.Single(items, i => i.Name == "demo.zip");
        Assert.False(zip.IsDirectory);
        Assert.Contains("格式 34", zip.Detail);
        Assert.Contains("Demo", zip.Detail);
        Assert.True(zip.Bytes > 0);
        Assert.Contains("格式 15", Assert.Single(items, i => i.IsDirectory).Detail);
    }

    [Fact]
    public async Task ImportResourcePackCopiesWithoutOverwriting()
    {
        using var fixture = new ContentFixture();
        var source = Path.Combine(fixture.Root, "pack.zip");
        WriteZip(source, ("pack.mcmeta", """{"pack":{"pack_format":22}}"""));

        var first = await fixture.Service.ImportAsync(
            fixture.Instance, ContentKind.ResourcePacks, source);
        var second = await fixture.Service.ImportAsync(
            fixture.Instance, ContentKind.ResourcePacks, source);

        Assert.Equal("pack.zip", first.Name);
        Assert.Equal("pack (1).zip", second.Name);
        Assert.True(File.Exists(first.FullPath));
        Assert.True(File.Exists(second.FullPath));
    }

    [Fact]
    public async Task ImportSaveZipRequiresLevelDat()
    {
        using var fixture = new ContentFixture();
        var good = Path.Combine(fixture.Root, "world.zip");
        WriteZip(good, ("level.dat", "fake-nbt"));
        var created = await fixture.Service.ImportAsync(
            fixture.Instance, ContentKind.Saves, good);
        Assert.True(created.IsDirectory);
        Assert.Equal("world", created.Name);
        Assert.True(File.Exists(Path.Combine(created.FullPath, "level.dat")));

        var bad = Path.Combine(fixture.Root, "broken.zip");
        WriteZip(bad, ("readme.txt", "not a world"));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.ImportAsync(fixture.Instance, ContentKind.Saves, bad));
        Assert.False(Directory.Exists(
            Path.Combine(fixture.Service.GetDirectory(fixture.Instance, ContentKind.Saves), "broken")));
    }

    [Fact]
    public async Task ImportRejectsZipPathTraversal()
    {
        using var fixture = new ContentFixture();
        var evil = Path.Combine(fixture.Root, "evil.zip");
        using (var archive = ZipFile.Open(evil, ZipArchiveMode.Create))
        {
            using (var level = archive.CreateEntry("level.dat").Open())
            {
                level.Write(Encoding.UTF8.GetBytes("ok"));
            }

            using (var escape = archive.CreateEntry("../escape.txt").Open())
            {
                escape.Write(Encoding.UTF8.GetBytes("bad"));
            }
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.ImportAsync(fixture.Instance, ContentKind.Saves, evil));
        Assert.False(File.Exists(Path.Combine(fixture.Instance.GameDirectory, "escape.txt")));
    }

    [Fact]
    public async Task ToggleResourcePackEditsOptionsWithBackup()
    {
        using var fixture = new ContentFixture();
        var directory = fixture.Service.GetDirectory(fixture.Instance, ContentKind.ResourcePacks);
        Directory.CreateDirectory(directory);
        WriteZip(
            Path.Combine(directory, "demo.zip"),
            ("pack.mcmeta", """{"pack":{"pack_format":34}}"""));
        var options = Path.Combine(fixture.Instance.GameDirectory, "options.txt");
        await File.WriteAllTextAsync(options, "fov:0.5\nresourcePacks:[\"vanilla\"]\n");

        var item = Assert.Single(
            await fixture.Service.ListAsync(fixture.Instance, ContentKind.ResourcePacks));
        Assert.False(item.IsEnabled);

        await fixture.Service.SetResourcePackEnabledAsync(fixture.Instance, item, true);
        Assert.True(File.Exists(options + ".qs-backup"));
        var enabled = Assert.Single(
            await fixture.Service.ListAsync(fixture.Instance, ContentKind.ResourcePacks));
        Assert.True(enabled.IsEnabled);
        Assert.Contains("file/demo.zip", await File.ReadAllTextAsync(options));

        await fixture.Service.SetResourcePackEnabledAsync(fixture.Instance, enabled, false);
        var disabled = Assert.Single(
            await fixture.Service.ListAsync(fixture.Instance, ContentKind.ResourcePacks));
        Assert.False(disabled.IsEnabled);
        var lines = await File.ReadAllLinesAsync(options);
        Assert.Contains("fov:0.5", lines);
        Assert.DoesNotContain(lines, line => line.Contains("file/demo.zip"));
    }

    [Fact]
    public async Task BackupSaveCreatesZipInPortableBackups()
    {
        using var fixture = new ContentFixture();
        var good = Path.Combine(fixture.Root, "world.zip");
        WriteZip(good, ("level.dat", "fake-nbt"), ("region/r.0.0.mca", "region"));
        var save = await fixture.Service.ImportAsync(
            fixture.Instance, ContentKind.Saves, good);

        var backup = await fixture.Service.BackupSaveAsync(save);

        Assert.True(File.Exists(backup));
        Assert.StartsWith(
            Path.Combine(fixture.Paths.DataRoot, "backups", "saves"),
            backup,
            StringComparison.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(backup);
        Assert.Contains(archive.Entries, e => e.FullName == "level.dat");
        Assert.Contains(archive.Entries, e => e.FullName == "region/r.0.0.mca");
    }

    [Fact]
    public async Task DeleteMovesContentIntoPortableQuarantine()
    {
        using var fixture = new ContentFixture();
        var directory = fixture.Service.GetDirectory(fixture.Instance, ContentKind.ShaderPacks);
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "shiny.zip");
        WriteZip(source, ("shaders/shader.fsh", "void main() {}"));
        var item = Assert.Single(
            await fixture.Service.ListAsync(fixture.Instance, ContentKind.ShaderPacks));

        var quarantined = await fixture.Service.DeleteToQuarantineAsync(item);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(quarantined));
        Assert.StartsWith(
            Path.Combine(fixture.Paths.DataRoot, "quarantine"),
            quarantined,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shiny.zip", quarantined);
    }

    [Fact]
    public async Task CopyInstanceCopiesContentAndRewritesIdentity()
    {
        using var fixture = new ContentFixture();
        var instances = new InstanceService(fixture.Paths);
        var source = await instances.CreateAsync("Copy Me", "26.3", "26.3");
        File.WriteAllText(Path.Combine(source.ModsDirectory, "demo.jar"), "jar");
        var sourceId = source.Manifest.Id;

        var copy = await instances.CopyAsync(source);

        Assert.NotEqual(sourceId, copy.Manifest.Id);
        Assert.Equal("Copy Me 副本", copy.Manifest.Name);
        Assert.True(File.Exists(Path.Combine(copy.ModsDirectory, "demo.jar")));
        Assert.True(Directory.Exists(Path.Combine(copy.GameDirectory, "saves")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(copy.Directory, "natives")));
        var loaded = await instances.TryLoadAsync(copy.Directory);
        Assert.NotNull(loaded);
        Assert.Equal(copy.Manifest.Id, loaded.Manifest.Id);
        Assert.NotEqual(sourceId, loaded.Manifest.Id);
    }

    private static void WriteZip(string path, params (string Name, string Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    private sealed class ContentFixture : IDisposable
    {
        public ContentFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "qs-content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Paths = new PortablePaths(Root);
            Paths.EnsureLayout();
            Service = new InstanceContentService(Paths);
            var instanceDirectory = Path.Combine(Paths.InstancesPath, "test");
            Directory.CreateDirectory(Path.Combine(instanceDirectory, "game"));
            Instance = new InstanceInfo
            {
                Directory = instanceDirectory,
                Manifest = new InstanceManifest
                {
                    Id = "test",
                    Name = "Test",
                    VersionId = "26.3",
                    MinecraftVersion = "26.3"
                }
            };
        }

        public string Root { get; }

        public PortablePaths Paths { get; }

        public InstanceContentService Service { get; }

        public InstanceInfo Instance { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
                }
            }
            catch
            {
                // Best-effort cleanup of temporary test data.
            }
        }
    }
}
