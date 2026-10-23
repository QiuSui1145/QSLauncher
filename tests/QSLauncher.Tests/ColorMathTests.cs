using QSLauncher.Core.Models;

namespace QSLauncher.Tests;

public sealed class ColorMathTests
{
    [Theory]
    [InlineData("#4ADE80", 0x4A, 0xDE, 0x80)]
    [InlineData("4ade80", 0x4A, 0xDE, 0x80)]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC)]
    [InlineData("#804ADE80", 0x4A, 0xDE, 0x80)]
    public void ParsesHexInCommonFormats(string input, byte r, byte g, byte b)
    {
        Assert.True(ColorMath.TryParseHex(input, out var color));
        Assert.Equal(new RgbColor(r, g, b), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("not-a-color")]
    public void RejectsInvalidHex(string input)
    {
        Assert.False(ColorMath.TryParseHex(input, out _));
        Assert.Equal("#4ADE80", ColorMath.NormalizeHex(input));
    }

    [Fact]
    public void NormalizeHexUsesFallback()
    {
        Assert.Equal("#123456", ColorMath.NormalizeHex("#123456", "#000000"));
        Assert.Equal("#000000", ColorMath.NormalizeHex(null, "#000000"));
    }

    [Fact]
    public void ContrastRatioMatchesWcagExtremes()
    {
        var black = new RgbColor(0, 0, 0);
        var white = new RgbColor(255, 255, 255);
        Assert.Equal(21, ColorMath.ContrastRatio(black, white), 3);
        Assert.Equal(1, ColorMath.ContrastRatio(black, black), 3);
        Assert.Equal(
            ColorMath.ContrastRatio(black, white),
            ColorMath.ContrastRatio(white, black),
            3);
    }

    [Fact]
    public void ReadableForegroundPicksTheStrongerContrast()
    {
        Assert.Equal(new RgbColor(250, 252, 255), ColorMath.ReadableForeground(new RgbColor(20, 20, 20)));
        Assert.Equal(new RgbColor(10, 14, 20), ColorMath.ReadableForeground(new RgbColor(240, 240, 240)));
    }

    [Fact]
    public void BlendClampsAndInterpolates()
    {
        var from = new RgbColor(10, 20, 30);
        var to = new RgbColor(110, 120, 130);
        Assert.Equal(new RgbColor(60, 70, 80), ColorMath.Blend(from, to, 0.5));
        Assert.Equal(from, ColorMath.Blend(from, to, -1));
        Assert.Equal(to, ColorMath.Blend(from, to, 2));
    }

    [Fact]
    public void HsvRoundTripsPrimaryColors()
    {
        foreach (var original in new[]
                 {
                     new RgbColor(255, 0, 0),
                     new RgbColor(0, 255, 0),
                     new RgbColor(0, 0, 255),
                     new RgbColor(74, 222, 128),
                     new RgbColor(128, 128, 128)
                 })
        {
            var back = ColorMath.FromHsv(ColorMath.ToHsv(original));
            Assert.InRange(Math.Abs(back.R - original.R), 0, 1);
            Assert.InRange(Math.Abs(back.G - original.G), 0, 1);
            Assert.InRange(Math.Abs(back.B - original.B), 0, 1);
        }
    }
}
