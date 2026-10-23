using System.Globalization;

namespace QSLauncher.Core.Models;

/// <summary>A small, UI-framework independent RGB color.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public double RelativeLuminance
    {
        get
        {
            static double Channel(byte value)
            {
                var c = value / 255d;
                return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(R)) + (0.7152 * Channel(G)) + (0.0722 * Channel(B));
        }
    }
}

public readonly record struct HsvColor(double Hue, double Saturation, double Value);

public static class ColorMath
{
    public static bool TryParseHex(string? value, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim().TrimStart('#');
        if (text.Length == 3)
        {
            text = string.Concat(text[0], text[0], text[1], text[1], text[2], text[2]);
        }
        else if (text.Length == 8)
        {
            // Accept #AARRGGBB but use the opaque RGB portion; launcher accents are opaque.
            text = text[2..];
        }

        if (text.Length != 6 ||
            !byte.TryParse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(text[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(text[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new RgbColor(r, g, b);
        return true;
    }

    public static string NormalizeHex(string? value, string fallback = "#4ADE80")
        => TryParseHex(value, out var color) ? color.ToHex() : fallback;

    public static double ContrastRatio(RgbColor first, RgbColor second)
    {
        var a = first.RelativeLuminance;
        var b = second.RelativeLuminance;
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static RgbColor ReadableForeground(RgbColor background)
    {
        var black = new RgbColor(10, 14, 20);
        var white = new RgbColor(250, 252, 255);
        return ContrastRatio(background, white) >= ContrastRatio(background, black) ? white : black;
    }

    public static RgbColor Blend(RgbColor from, RgbColor to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new RgbColor(
            (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
            (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
            (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    public static RgbColor Lighten(RgbColor color, double amount)
        => Blend(color, new RgbColor(255, 255, 255), amount);

    public static RgbColor Darken(RgbColor color, double amount)
        => Blend(color, new RgbColor(0, 0, 0), amount);

    public static HsvColor ToHsv(RgbColor color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue;
        if (delta < 0.00001)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        var saturation = max <= 0 ? 0 : delta / max;
        return new HsvColor(hue, saturation, max);
    }

    public static RgbColor FromHsv(HsvColor hsv)
    {
        var hue = ((hsv.Hue % 360) + 360) % 360;
        var saturation = Math.Clamp(hsv.Saturation, 0, 1);
        var value = Math.Clamp(hsv.Value, 0, 1);
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var m = value - chroma;

        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return new RgbColor(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
