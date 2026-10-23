using System.Text.Json;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.Tests;

public sealed class MojangStaticTests
{
    [Theory]
    [InlineData("net.minecraft:client:26.3", "net/minecraft/client/26.3/client-26.3.jar")]
    [InlineData("org.lwjgl:lwjgl:3.3.3:natives-windows",
        "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows.jar")]
    [InlineData("com.example:demo:1.0@zip", "com/example/demo/1.0/demo-1.0.zip")]
    public void BuildsMavenPaths(string name, string expected)
    {
        Assert.Equal(expected, MojangService.GetMavenPath(name));
    }

    [Fact]
    public void CombinesRelativeAndAbsoluteUrls()
    {
        Assert.Equal(
            "https://libraries.minecraft.net/com/example/demo.jar",
            MojangService.CombineUrl("https://libraries.minecraft.net", "com/example/demo.jar"));
        Assert.Equal(
            "https://cdn.example.test/file.jar",
            MojangService.CombineUrl("https://ignored.test/", "https://cdn.example.test/file.jar"));
    }

    [Fact]
    public void ParsesConditionalArguments()
    {
        using var document = JsonDocument.Parse(
            """
            [
              "--demo",
              { "rules": [ { "action": "allow" } ], "value": ["--enabled", "--second"] },
              { "rules": [ { "action": "disallow" } ], "value": ["--disabled"] }
            ]
            """);
        var arguments = MojangService.ParseArguments(
            document.RootElement.EnumerateArray().Select(e => e.Clone()).ToList());
        Assert.Equal(3, arguments.Count);
        Assert.Equal("--demo", arguments[0].Values[0]);
        Assert.Equal(["--enabled", "--second"], arguments[1].Values);
        Assert.True(MojangService.RulesAllow(arguments[1].Rules));
        Assert.False(MojangService.RulesAllow(arguments[2].Rules));
    }

    [Fact]
    public void RulesUseLastMatchingActionAndFeatures()
    {
        using var document = JsonDocument.Parse(
            """
            [
              { "action": "allow" },
              { "action": "disallow", "features": { "has_custom_resolution": true } }
            ]
            """);
        var rules = MojangService.ParseRules(document.RootElement);
        Assert.NotNull(rules);
        Assert.True(MojangService.RulesAllow(
            rules,
            new Dictionary<string, bool> { ["has_custom_resolution"] = false }));
        Assert.False(MojangService.RulesAllow(
            rules,
            new Dictionary<string, bool> { ["has_custom_resolution"] = true }));
    }

    [Fact]
    public void NoRulesMeansAllowed()
    {
        Assert.True(MojangService.RulesAllow(null));
        Assert.True(MojangService.RulesAllow([]));
    }

    [Fact]
    public void IgnoredRulesStayWithinTheirAction()
    {
        using var document = JsonDocument.Parse(
            """
            [
              { "action": "allow", "os": { "name": "definitely-not-this-os" } }
            ]
            """);
        var rules = MojangService.ParseRules(document.RootElement);
        Assert.False(MojangService.RulesAllow(rules));
    }
}
