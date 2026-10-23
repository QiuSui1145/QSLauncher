using System.IO.Compression;
using QSLauncher.Core.Services;

namespace QSLauncher.Tests;

public sealed class ModpackFormatTests
{
    [Theory]
    [InlineData("modrinth.index.json", "{\"formatVersion\":1}", false)]
    [InlineData("manifest.json", "{\"manifestType\":\"minecraftModpack\",\"minecraft\":{\"version\":\"1.20.1\"}}", false)]
    [InlineData("mmc-pack.json", "{\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"1.20.1\"}]}", false)]
    [InlineData("manifest.json", "{\"name\":\"config-only\"}", true)]
    [InlineData("mods/example.jar", "jar", true)]
    public void DetectsKnownAndGenericArchives(string entryName, string content, bool generic)
    {
        var path = Path.Combine(Path.GetTempPath(), $"qslauncher-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }

            Assert.Equal(generic, ModpackInstaller.IsGenericArchive(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
