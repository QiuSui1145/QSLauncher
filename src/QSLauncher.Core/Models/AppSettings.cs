namespace QSLauncher.Core.Models;

public enum ThemeMode
{
    System,
    Dark,
    Light
}

public enum BackgroundMode
{
    None,
    Image,
    Video
}

public enum BackgroundFit
{
    Fill,
    Fit,
    Tile
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public AppearanceSettings Appearance { get; set; } = new();

    public AccountSettings Accounts { get; set; } = new();

    public DownloadSettings Downloads { get; set; } = new();

    public JavaSettings Java { get; set; } = new();

    public MicrosoftSettings Microsoft { get; set; } = new();

    public GameSettings Game { get; set; } = new();

    public HomeLayoutSettings Home { get; set; } = HomeLayoutDefaults.Create();

    public WindowSettings Window { get; set; } = new();

    public string Language { get; set; } = "zh-CN";
}

public sealed class AppearanceSettings
{
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    public string AccentColor { get; set; } = "#4ADE80";

    /// <summary>Optional custom logo, relative to the portable data directory.</summary>
    public string? LogoPath { get; set; }

    public BackgroundSettings Background { get; set; } = new();

    public GlassSettings Glass { get; set; } = new();
}

public sealed class BackgroundSettings
{
    public BackgroundMode Mode { get; set; } = BackgroundMode.None;

    /// <summary>Path relative to the portable data directory.</summary>
    public string? Path { get; set; }

    public BackgroundFit Fit { get; set; } = BackgroundFit.Fill;

    /// <summary>0..1 dark scrim over the media, for text contrast.</summary>
    public double Dim { get; set; } = 0.45;

    /// <summary>0..40 pixel blur applied to the background layer.</summary>
    public double Blur { get; set; } = 0;

    public bool Muted { get; set; } = true;

    public bool Loop { get; set; } = true;

    public bool PauseWhenInactive { get; set; } = true;
}

public sealed class GlassSettings
{
    /// <summary>0..1 opacity of glass surfaces.</summary>
    public double Opacity { get; set; } = 0.72;

    /// <summary>0..40 visual blur strength for the background behind glass.</summary>
    public double Blur { get; set; } = 18;

    /// <summary>0..1 subtle grain overlay.</summary>
    public double Noise { get; set; } = 0.06;
}

public sealed class AccountSettings
{
    public string? LastAccountId { get; set; }
}

public sealed class DownloadSettings
{
    public int Concurrency { get; set; } = 8;

    public int TimeoutSeconds { get; set; } = 30;

    public bool UseSystemProxy { get; set; } = true;
}

public sealed class JavaSettings
{
    public string? DefaultRuntimeId { get; set; }

    public string? CustomJavaPath { get; set; }
}

public sealed class MicrosoftSettings
{
    /// <summary>
    /// Public client id of a user-owned Azure application.
    /// QS Launcher never bundles a client id or a client secret.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;
}

public enum GameRootMountMode
{
    /// <summary>Only the selected .minecraft directory is mounted at a time.</summary>
    Single,

    /// <summary>Every registered .minecraft directory is mounted together.</summary>
    Multiple
}

public sealed class GameSettings
{
    /// <summary>
    /// Extra .minecraft directories. Versions are read in place and appear in
    /// the normal instance library, tagged with the directory's display name.
    /// </summary>
    public List<GameRootSetting> Roots { get; set; } = new();

    public GameRootMountMode MountMode { get; set; } = GameRootMountMode.Single;

    /// <summary>Absolute path of the single mounted root, or null for managed instances.</summary>
    public string? ActiveRoot { get; set; }
}

public sealed class GameRootSetting
{
    public string Path { get; set; } = string.Empty;

    /// <summary>User-assigned, unique display name used as the version tag.</summary>
    public string? Name { get; set; }
}

public sealed class HomeLayoutSettings
{
    public List<HomeWidgetSetting> Widgets { get; set; } = new();
}

public sealed class HomeWidgetSetting
{
    public string Key { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Visible { get; set; } = true;
}

public static class HomeWidgetKeys
{
    public const string Launch = "launch";
    public const string Quick = "quick";
    public const string Stats = "stats";
    public const string Recent = "recent";
}

public static class HomeLayoutDefaults
{
    public static HomeLayoutSettings Create() => new()
    {
        Widgets =
        [
            new() { Key = HomeWidgetKeys.Launch, X = 0, Y = 0, Width = 600, Height = 250 },
            new() { Key = HomeWidgetKeys.Quick, X = 612, Y = 0, Width = 268, Height = 250 },
            new() { Key = HomeWidgetKeys.Stats, X = 0, Y = 250, Width = 880, Height = 100 },
            new() { Key = HomeWidgetKeys.Recent, X = 0, Y = 360, Width = 880, Height = 220 }
        ]
    };

    public static HomeWidgetSetting Get(HomeLayoutSettings layout, string key)
    {
        var widget = layout.Widgets.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        if (widget is not null)
        {
            return widget;
        }

        widget = Create().Widgets.First(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        layout.Widgets.Add(widget);
        return widget;
    }
}

public sealed class WindowSettings
{
    public double Width { get; set; } = 1280;

    public double Height { get; set; } = 800;

    public double Left { get; set; } = double.NaN;

    public double Top { get; set; } = double.NaN;

    public bool Maximized { get; set; }
}

public static class SettingsDefaults
{
    public static AppSettings Create() => new();

    public static AppearanceSettings CreateAppearance() => new();
}
