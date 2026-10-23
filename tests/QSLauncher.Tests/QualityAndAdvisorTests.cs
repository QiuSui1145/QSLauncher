using System.IO.Compression;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.Tests;

public sealed class QualityAndAdvisorTests
{
    [Fact]
    public void MemoryAdviceScalesWithInstalledContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "qs-memory-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Path.Combine(root, "game");
            var mods = Path.Combine(game, "mods");
            var resourcePacks = Path.Combine(game, "resourcepacks");
            var shaderPacks = Path.Combine(game, "shaderpacks");
            Directory.CreateDirectory(mods);
            Directory.CreateDirectory(resourcePacks);
            Directory.CreateDirectory(shaderPacks);
            for (var i = 0; i < 10; i++)
            {
                File.WriteAllBytes(Path.Combine(mods, $"mod{i}.jar"), []);
            }

            Directory.CreateDirectory(Path.Combine(resourcePacks, "pack-a"));
            Directory.CreateDirectory(Path.Combine(resourcePacks, "pack-b"));
            Directory.CreateDirectory(Path.Combine(shaderPacks, "shader-a"));

            var instance = new InstanceInfo
            {
                Directory = root,
                Manifest = new InstanceManifest
                {
                    Id = "memory",
                    Name = "memory",
                    VersionId = "1.21.1",
                    MinecraftVersion = "1.21.1",
                    Loader = LoaderKind.Fabric
                }
            };

            var recommendation = MemoryAdvisor.Recommend(
                instance, new MemorySnapshot(32768, 16384));

            Assert.Equal(10, recommendation.ModCount);
            Assert.Equal(2, recommendation.ResourcePackCount);
            Assert.Equal(1, recommendation.ShaderPackCount);
            Assert.Equal(14336, recommendation.MemoryMb);
            Assert.Contains("10 个模组", recommendation.Reason);
            Assert.Contains("本机剩余 16384 MB", recommendation.Reason);
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
    public void VanillaInstanceUsesAvailableMemoryInsteadOfFixedBaseline()
    {
        var root = Path.Combine(Path.GetTempPath(), "qs-memory-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var instance = new InstanceInfo
            {
                Directory = root,
                Manifest = new InstanceManifest
                {
                    Id = "vanilla",
                    Name = "vanilla",
                    VersionId = "1.21.1",
                    MinecraftVersion = "1.21.1"
                }
            };

            var recommendation = MemoryAdvisor.Recommend(
                instance, new MemorySnapshot(16384, 8192));

            Assert.Equal(7168, recommendation.MemoryMb);
            Assert.Equal(0, recommendation.ModCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Theory]
    [InlineData("java.lang.OutOfMemoryError: Java heap space", "内存不足")]
    [InlineData("DuplicateModsFoundException: duplicate mod 'sodium' found", "发现重复模组")]
    [InlineData("Mixin apply failed for create", "Mixin 注入失败")]
    [InlineData("java.lang.UnsupportedClassVersionError", "Java 版本不匹配")]
    [InlineData("Address already in use: bind", "端口被占用")]
    public void LogAnalyzerRecognizesCommonFailures(string line, string expectedTitle)
    {
        var finding = LogAnalyzer.Analyze(line);

        Assert.NotNull(finding);
        Assert.Equal(expectedTitle, finding.Title);
        Assert.False(string.IsNullOrWhiteSpace(finding.Suggestion));
    }

    [Fact]
    public void LogAnalyzerIgnoresOrdinaryLines()
    {
        Assert.Null(LogAnalyzer.Analyze("[12:00:00] [Render thread/INFO]: Loaded 1234 recipes"));
    }

    [Fact]
    public void DescriptionLocalizerPrefersChineseAndAddsSummary()
    {
        Assert.Equal("一个中文简介", DescriptionLocalizer.Summarize("x", "一个中文简介"));
        Assert.Contains("钠", DescriptionLocalizer.Summarize("Sodium", "Fast rendering"));
        Assert.Contains("科技", DescriptionLocalizer.Summarize(
            "Unknown project", "A big machine mod", ["technology"]));
        Assert.StartsWith("英文简介", DescriptionLocalizer.Summarize(
            "Unknown project", "Something entirely unrelated"));
    }

    [Fact]
    public void SearchTermsCoverChinesePackNames()
    {
        var terms = SearchTerms.Expand("匠魂 光影");

        Assert.Contains("tinkers", terms);
        Assert.Contains("shader", terms);
    }

    [Fact]
    public void GenericZipWithVersionJsonDoesNotNeedManualFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "qs-pack-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(root, "pack.zip");
        Directory.CreateDirectory(root);
        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("version.json");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("{\"id\":\"1.20.1-forge-47.3.22\"}");
            }

            Assert.False(ModpackInstaller.NeedsFallback(archivePath));
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
    public void HomeLayoutDefaultsAreUniqueAndVisible()
    {
        var layout = HomeLayoutDefaults.Create();

        Assert.Equal(4, layout.Widgets.Count);
        Assert.Equal(4, layout.Widgets.Select(widget => widget.Key).Distinct().Count());
        Assert.All(layout.Widgets, widget => Assert.True(widget.Visible));
    }
}
