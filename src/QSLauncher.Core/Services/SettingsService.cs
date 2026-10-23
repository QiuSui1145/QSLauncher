using System.Text.Json;
using System.Text.Json.Serialization;
using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly PortablePaths _paths;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public SettingsService(PortablePaths paths)
    {
        _paths = paths;
    }

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.DataRoot);

        var loaded = await TryReadAsync(_paths.ConfigPath, cancellationToken).ConfigureAwait(false);
        if (loaded is null)
        {
            loaded = await TryReadAsync(_paths.ConfigBackupPath, cancellationToken).ConfigureAwait(false);
        }

        Current = Normalize(loaded ?? new AppSettings());
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Current = Normalize(Current);
            Directory.CreateDirectory(_paths.DataRoot);

            if (File.Exists(_paths.ConfigPath))
            {
                File.Copy(_paths.ConfigPath, _paths.ConfigBackupPath, overwrite: true);
            }

            var tempPath = _paths.ConfigPath + ".tmp";
            await using (var stream = new FileStream(
                             tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _paths.ConfigPath, overwrite: true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void Replace(AppSettings settings)
    {
        Current = Normalize(settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        Current = new AppSettings();
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AppSettings?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.SchemaVersion = 1;
        settings.Appearance ??= new AppearanceSettings();
        settings.Appearance.Background ??= new BackgroundSettings();
        settings.Appearance.Glass ??= new GlassSettings();
        settings.Appearance.AccentColor = ColorMath.NormalizeHex(settings.Appearance.AccentColor);

        var background = settings.Appearance.Background;
        background.Dim = Math.Clamp(background.Dim, 0, 0.9);
        background.Blur = Math.Clamp(background.Blur, 0, 40);
        if (background.Mode == BackgroundMode.None)
        {
            background.Path = null;
        }

        if (background.Mode is BackgroundMode.Image or BackgroundMode.Video &&
            string.IsNullOrWhiteSpace(background.Path))
        {
            background.Mode = BackgroundMode.None;
        }

        if (background.Mode == BackgroundMode.None)
        {
            background.Path = null;
        }

        settings.Appearance.Glass.Opacity = Math.Clamp(settings.Appearance.Glass.Opacity, 0, 1);
        settings.Appearance.Glass.Blur = Math.Clamp(settings.Appearance.Glass.Blur, 0, 40);
        settings.Appearance.Glass.Noise = Math.Clamp(settings.Appearance.Glass.Noise, 0, 0.25);

        settings.Downloads ??= new DownloadSettings();
        settings.Downloads.Concurrency = Math.Clamp(settings.Downloads.Concurrency, 1, 32);
        settings.Downloads.TimeoutSeconds = Math.Clamp(settings.Downloads.TimeoutSeconds, 5, 300);

        settings.Java ??= new JavaSettings();
        settings.Microsoft ??= new MicrosoftSettings();
        settings.Microsoft.ClientId = (settings.Microsoft.ClientId ?? string.Empty).Trim();
        settings.Accounts ??= new AccountSettings();

        settings.Game ??= new GameSettings();
        settings.Game.Roots ??= new List<GameRootSetting>();
        var rootPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedRoots = new List<GameRootSetting>();
        foreach (var root in settings.Game.Roots)
        {
            var path = (root.Path ?? string.Empty).Trim();
            if (path.Length == 0 || !rootPaths.Add(path))
            {
                continue;
            }

            var name = (root.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            }

            if (name.Length == 0)
            {
                name = "游戏目录";
            }

            var unique = name;
            var suffix = 2;
            while (!rootNames.Add(unique))
            {
                unique = $"{name}-{suffix++}";
            }

            normalizedRoots.Add(new GameRootSetting { Path = path, Name = unique });
        }

        settings.Game.Roots = normalizedRoots;
        if (!Enum.IsDefined(settings.Game.MountMode))
        {
            settings.Game.MountMode = GameRootMountMode.Single;
        }

        if (!string.IsNullOrWhiteSpace(settings.Game.ActiveRoot)
            && normalizedRoots.All(root => !string.Equals(
                root.Path, settings.Game.ActiveRoot, StringComparison.OrdinalIgnoreCase)))
        {
            settings.Game.ActiveRoot = null;
        }

        settings.Home ??= HomeLayoutDefaults.Create();
        settings.Home.Widgets ??= new List<HomeWidgetSetting>();
        var defaults = HomeLayoutDefaults.Create();
        var keys = new[] { HomeWidgetKeys.Launch, HomeWidgetKeys.Quick, HomeWidgetKeys.Stats, HomeWidgetKeys.Recent };
        foreach (var key in keys)
        {
            var widget = settings.Home.Widgets.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (widget is null)
            {
                widget = defaults.Widgets.First(item => item.Key == key);
                settings.Home.Widgets.Add(widget);
            }

            widget.Key = key;
            widget.X = Math.Clamp(double.IsFinite(widget.X) ? widget.X : 0, 0, 2600);
            widget.Y = Math.Clamp(double.IsFinite(widget.Y) ? widget.Y : 0, 0, 2600);
            widget.Width = Math.Clamp(double.IsFinite(widget.Width) ? widget.Width : 400, 260, 2200);
            widget.Height = Math.Clamp(double.IsFinite(widget.Height) ? widget.Height : 200, 100, 1600);
        }

        settings.Home.Widgets = settings.Home.Widgets
            .Where(item => keys.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        settings.Window ??= new WindowSettings();
        settings.Window.Width = Math.Clamp(
            double.IsFinite(settings.Window.Width) ? settings.Window.Width : 1280, 980, 3840);
        settings.Window.Height = Math.Clamp(
            double.IsFinite(settings.Window.Height) ? settings.Window.Height : 800, 620, 2160);
        if (!double.IsFinite(settings.Window.Left))
        {
            settings.Window.Left = double.NaN;
        }

        if (!double.IsFinite(settings.Window.Top))
        {
            settings.Window.Top = double.NaN;
        }

        settings.Language = string.IsNullOrWhiteSpace(settings.Language) ? "zh-CN" : settings.Language;
        return settings;
    }
}
