using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.Tests;

public sealed class SearchAndExternalRootTests
{
    [Fact]
    public void RelatedKeywordsBecomeSeparateQueries()
    {
        var terms = SearchTerms.Expand("优化 小地图");

        Assert.Equal(["optimization", "performance", "minimap", "map"], terms);
    }

    [Fact]
    public void DuplicateAndEmptyTermsAreRemoved()
    {
        Assert.Equal(["foo"], SearchTerms.Expand("foo，foo foo"));
        Assert.Equal([string.Empty], SearchTerms.Expand("   "));
    }

    [Fact]
    public void KeywordExpansionIsCapped()
    {
        var terms = SearchTerms.Expand("优化 性能 科技 魔法 冒险 装饰 家具 农业", maxTerms: 6);

        Assert.Equal(6, terms.Count);
        Assert.Equal(terms.Count, terms.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void RelatedTermResultsAreInterleavedAndDeduplicated()
    {
        var batches = new[]
        {
            new[] { "opt-a", "opt-b", "opt-c" },
            new[] { "map-a", "opt-a", "map-b" }
        };

        var merged = SearchTerms.Merge<string>(
            batches.Select(batch => (IReadOnlyList<string>)batch),
            value => value,
            4);

        Assert.Equal(["opt-a", "map-a", "opt-b", "map-b"], merged);
    }

    [Theory]
    [InlineData("1.21.1-NeoForge_21.1.228", "1.21.1-NeoForge_21.1.228", "1.21.1")]
    [InlineData("1.20.1forge", "1.20.1forge", "1.20.1")]
    [InlineData("fabric-loader-1.20.1", "1.20.1", "1.20.1")]
    [InlineData("26.3", "26.3", "26.3")]
    public void ExternalLoaderIdsExposeTheirMinecraftVersion(
        string versionId,
        string baseId,
        string expected)
    {
        Assert.Equal(expected, GameRootService.DetectMinecraftVersion(versionId, baseId));
    }

    [Fact]
    public void ExternalRootIsUsedAsTheGameDirectoryInPlace()
    {
        var root = Path.Combine(Path.GetTempPath(), "qslauncher-external-root");
        var linked = new InstanceInfo
        {
            Directory = Path.Combine(Path.GetTempPath(), "qslauncher-instance-link"),
            Manifest = new InstanceManifest
            {
                Id = "linked",
                Name = "linked",
                VersionId = "1.21.1",
                MinecraftVersion = "1.21.1",
                SourceRoot = root,
                SourceVersionId = "1.21.1"
            }
        };

        Assert.Equal(root, linked.GameDirectory);
        Assert.Equal(Path.Combine(root, "mods"), linked.ModsDirectory);
    }

    [Fact]
    public void ExternalIsolatedVersionUsesItsVersionFolderAsGameDirectory()
    {
        var temp = Path.Combine(Path.GetTempPath(), "qslauncher-isolated-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, ".minecraft");
        var versionDirectory = Path.Combine(root, "versions", "1.20.1-forge");
        Directory.CreateDirectory(Path.Combine(versionDirectory, "mods"));
        Directory.CreateDirectory(Path.Combine(versionDirectory, "config"));
        try
        {
            var linked = new InstanceInfo
            {
                Directory = Path.Combine(temp, "instance-link"),
                Manifest = new InstanceManifest
                {
                    Id = "linked-isolated",
                    Name = "linked-isolated",
                    VersionId = "1.20.1-forge",
                    MinecraftVersion = "1.20.1",
                    SourceRoot = root,
                    SourceVersionId = "1.20.1-forge"
                }
            };

            Assert.Equal(versionDirectory, linked.GameDirectory);
            Assert.Equal(Path.Combine(versionDirectory, "mods"), linked.ModsDirectory);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    [Fact]
    public void ExternalVanillaVersionWithoutGameContentKeepsTheSharedRoot()
    {
        var temp = Path.Combine(Path.GetTempPath(), "qslauncher-vanilla-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, ".minecraft");
        var versionDirectory = Path.Combine(root, "versions", "1.21.1");
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(Path.Combine(versionDirectory, "1.21.1.json"), "{}");
        File.WriteAllText(Path.Combine(versionDirectory, "1.21.1.jar"), string.Empty);
        try
        {
            var linked = new InstanceInfo
            {
                Directory = Path.Combine(temp, "instance-link"),
                Manifest = new InstanceManifest
                {
                    Id = "linked-vanilla",
                    Name = "linked-vanilla",
                    VersionId = "1.21.1",
                    MinecraftVersion = "1.21.1",
                    SourceRoot = root,
                    SourceVersionId = "1.21.1"
                }
            };

            Assert.Equal(root, linked.GameDirectory);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    [Fact]
    public void ManagedInstanceKeepsItsOwnGameDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "qslauncher-instance-managed");
        var managed = new InstanceInfo
        {
            Directory = directory,
            Manifest = new InstanceManifest
            {
                Id = "managed",
                Name = "managed",
                VersionId = "1.21.1",
                MinecraftVersion = "1.21.1"
            }
        };

        Assert.Equal(Path.Combine(directory, "game"), managed.GameDirectory);
        Assert.Equal(Path.Combine(directory, "game", "mods"), managed.ModsDirectory);
    }
}
