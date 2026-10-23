using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;
using ThemeMode = QSLauncher.Core.Models.ThemeMode;

namespace QSLauncher.App.Services;

public sealed class ThemeService
{
    private readonly SettingsService _settings;
    private readonly List<string> _appliedKeys = new();
    private bool _subscribed;

    public ThemeService(SettingsService settings)
    {
        _settings = settings;
    }

    public bool IsLight { get; private set; }

    public event EventHandler? ThemeChanged;

    public void Initialize()
    {
        if (!_subscribed)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _subscribed = true;
        }

        Apply();
    }

    public void Apply()
    {
        var appearance = _settings.Current.Appearance;
        IsLight = appearance.ThemeMode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => IsSystemLightTheme()
        };

        if (!ColorMath.TryParseHex(appearance.AccentColor, out var accent))
        {
            accent = new RgbColor(0x4A, 0xDE, 0x80);
        }

        var palette = IsLight ? LightPalette : DarkPalette;
        SetBrush("BackgroundBrush", palette.Background);
        SetBrush("SurfaceBrush", palette.Surface);
        SetBrush("SurfaceElevatedBrush", palette.SurfaceElevated);
        SetBrush("TextPrimaryBrush", palette.TextPrimary);
        SetBrush("TextSecondaryBrush", palette.TextSecondary);
        SetBrush("TextTertiaryBrush", palette.TextTertiary);
        SetBrush("DividerBrush", palette.Border);
        SetBrush("InputBrush", palette.Input);
        SetBrush("InputBorderBrush", palette.BorderStrong);
        SetBrush("NavHoverBrush", palette.NavHover);
        SetBrush("OverlayBrush", palette.Overlay);
        SetBrush("ShadowBrush", palette.Shadow);
        SetBrush("DangerBrush", new RgbColor(0xFF, 0x6B, 0x6B));
        SetBrush("WarningBrush", new RgbColor(0xF5, 0xB9, 0x42));
        SetBrush("SuccessBrush", new RgbColor(0x4A, 0xDE, 0x80));

        // The wallpaper is never covered by a full-surface tint. Only the
        // panels themselves are translucent, so the chosen image or video
        // keeps its original brightness and colour.
        var glassOpacity = Math.Clamp(appearance.Glass.Opacity, 0, 1);
        var glassAlpha = (byte)Math.Round(glassOpacity * 255);

        // "磨砂强度" changes the frost grain, not the wallpaper. The panel
        // keeps a 10% stronger grain than the flyout so menus stay legible.
        var frost = Math.Clamp(appearance.Glass.Blur, 0, 40) / 40.0;
        var panelNoise = 0.50 + frost * 0.30;
        var flyoutNoise = panelNoise / 1.10;
        Application.Current.Resources["PanelNoiseOpacity"] = panelNoise;
        Application.Current.Resources["FlyoutNoiseOpacity"] = flyoutNoise;
        Application.Current.Resources["ComboNoiseOpacity"] = flyoutNoise;
        Application.Current.Resources["ButtonNoiseOpacity"] = 0.22 + frost * 0.18;
        var strongAlpha = (byte)Math.Round(
            Math.Clamp(0.52 + glassOpacity * 0.32, 0.52, 0.82) * 255);
        var frostedBase = IsLight
            ? ColorMath.Blend(palette.SurfaceElevated, new RgbColor(126, 144, 152), 0.10)
            : palette.SurfaceElevated;
        var flyoutBase = IsLight
            ? ColorMath.Blend(palette.SurfaceElevated, new RgbColor(126, 144, 152), 0.05)
            : palette.SurfaceElevated;
        SetBrush("GlassBrush", frostedBase, glassAlpha);
        SetBrush("GlassStrongBrush", frostedBase, strongAlpha);
        SetBrush("GlassBorderBrush", new RgbColor(255, 255, 255), IsLight ? (byte)110 : (byte)42);

        // Flyouts and secondary controls use their own translucent material.
        // They stay readable over a wallpaper while no longer looking like
        // opaque white rectangles in the light theme. The flyout sits slightly
        // above the panel (more opaque) so the two frosted layers contrast.
        var cardAlpha = (byte)Math.Round(Math.Clamp(0.60 + glassOpacity * 0.22, 0.60, 0.84) * 255);
        var flyoutAlpha = (byte)Math.Round(Math.Clamp(0.74 + glassOpacity * 0.14, 0.74, 0.90) * 255);
        var buttonAlpha = (byte)Math.Round(Math.Clamp(0.38 + glassOpacity * 0.25, 0.38, 0.64) * 255);
        var buttonHoverAlpha = (byte)Math.Round(Math.Clamp(0.55 + glassOpacity * 0.22, 0.55, 0.78) * 255);
        SetBrush("CardBrush", frostedBase, cardAlpha);
        SetBrush("FlyoutBrush", flyoutBase, flyoutAlpha);
        SetBrush("FlyoutBorderBrush", new RgbColor(255, 255, 255), IsLight ? (byte)104 : (byte)54);
        SetBrush("FlyoutItemHoverBrush", accent, IsLight ? (byte)30 : (byte)46);
        var buttonBase = IsLight
            ? ColorMath.Blend(palette.Surface, new RgbColor(126, 144, 152), 0.06)
            : palette.Surface;
        var buttonTint = ColorMath.Blend(buttonBase, accent, IsLight ? 0.10 : 0.07);
        var buttonHoverTint = ColorMath.Blend(buttonBase, accent, IsLight ? 0.18 : 0.13);
        var buttonBorderTint = ColorMath.Blend(buttonBase, accent, IsLight ? 0.34 : 0.24);
        SetBrush("ButtonGlassBrush", buttonTint, buttonAlpha);
        SetBrush("ButtonGlassHoverBrush", buttonHoverTint, buttonHoverAlpha);
        SetBrush("ButtonGlassBorderBrush", buttonBorderTint, IsLight ? (byte)96 : (byte)54);

        SetBrush("TitleBarBrush", palette.Background, IsLight ? (byte)178 : (byte)112);
        SetBrush("SidebarBrush", palette.Background, IsLight ? (byte)192 : (byte)136);

        SetBrush("AccentBrush", accent);
        SetBrush("AccentHoverBrush", ColorMath.Lighten(accent, IsLight ? 0.12 : 0.16));
        SetBrush("AccentPressedBrush", ColorMath.Darken(accent, 0.16));
        SetBrush("AccentSoftBrush", accent, IsLight ? (byte)38 : (byte)48);
        SetBrush("AccentTextBrush", ColorMath.ReadableForeground(accent));
        SetBrush("SelectionBrush", accent, IsLight ? (byte)48 : (byte)56);

        // Keep the hero gradient in the same hue as the chosen accent so a
        // custom palette never fights a fixed teal banner.
        var heroStart = ColorMath.Lighten(accent, IsLight ? 0.02 : 0.04);
        var heroEnd = ColorMath.Darken(accent, IsLight ? 0.34 : 0.48);
        var gradient = new LinearGradientBrush(
            ToColor(heroStart),
            ToColor(heroEnd),
            new Point(0, 0),
            new Point(1, 1));
        gradient.Freeze();
        Application.Current.Resources["HeroGradientBrush"] = gradient;

        Application.Current.Resources["IsLightTheme"] = IsLight;
        Application.Current.Resources["ThemeAccentHex"] = accent.ToHex();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshSystemTheme()
    {
        if (_settings.Current.Appearance.ThemeMode == ThemeMode.System)
        {
            Apply();
        }
    }

    public static bool IsSystemLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 0);
            return value is int i && i != 0;
        }
        catch
        {
            return false;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color))
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(RefreshSystemTheme);
    }

    private void SetBrush(string key, RgbColor color, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        Application.Current.Resources[key] = brush;
        if (!_appliedKeys.Contains(key))
        {
            _appliedKeys.Add(key);
        }
    }

    private static Color ToColor(RgbColor color)
        => Color.FromRgb(color.R, color.G, color.B);

    private static readonly Palette DarkPalette = new(
        Background: new RgbColor(0x0B, 0x0F, 0x14),
        Surface: new RgbColor(0x12, 0x1A, 0x23),
        SurfaceElevated: new RgbColor(0x18, 0x23, 0x2E),
        TextPrimary: new RgbColor(0xF2, 0xF6, 0xF8),
        TextSecondary: new RgbColor(0xA9, 0xB7, 0xC3),
        TextTertiary: new RgbColor(0x9A, 0xAA, 0xB8),
        Border: new RgbColor(0x2A, 0x3A, 0x48),
        BorderStrong: new RgbColor(0x3A, 0x4D, 0x5D),
        Input: new RgbColor(0x0D, 0x14, 0x1B),
        NavHover: new RgbColor(0x1A, 0x25, 0x30),
        Overlay: new RgbColor(0x99, 0x00, 0x00),
        Shadow: new RgbColor(0x66, 0x00, 0x00));

    private static readonly Palette LightPalette = new(
        Background: new RgbColor(0xF3, 0xF6, 0xF8),
        Surface: new RgbColor(0xFF, 0xFF, 0xFF),
        SurfaceElevated: new RgbColor(0xF9, 0xFB, 0xFC),
        TextPrimary: new RgbColor(0x14, 0x20, 0x2B),
        TextSecondary: new RgbColor(0x46, 0x58, 0x69),
        TextTertiary: new RgbColor(0x5D, 0x6D, 0x7B),
        Border: new RgbColor(0xDA, 0xE3, 0xE9),
        BorderStrong: new RgbColor(0xC3, 0xD0, 0xD9),
        Input: new RgbColor(0xEE, 0xF3, 0xF6),
        NavHover: new RgbColor(0xE6, 0xEE, 0xF2),
        Overlay: new RgbColor(0x66, 0x00, 0x00),
        Shadow: new RgbColor(0x22, 0x00, 0x00));

    private sealed record Palette(
        RgbColor Background,
        RgbColor Surface,
        RgbColor SurfaceElevated,
        RgbColor TextPrimary,
        RgbColor TextSecondary,
        RgbColor TextTertiary,
        RgbColor Border,
        RgbColor BorderStrong,
        RgbColor Input,
        RgbColor NavHover,
        RgbColor Overlay,
        RgbColor Shadow);
}
