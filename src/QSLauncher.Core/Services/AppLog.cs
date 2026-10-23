using System.Globalization;

namespace QSLauncher.Core.Services;

/// <summary>Small append-only log. Never logs secrets by convention.</summary>
public sealed class AppLog
{
    private static readonly object Sync = new();
    private static string? _directory;

    public static void Initialize(string directory)
    {
        lock (Sync)
        {
            _directory = directory;
            Directory.CreateDirectory(directory);
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        lock (Sync)
        {
            if (_directory is null)
            {
                return;
            }

            try
            {
                var path = Path.Combine(_directory, $"launcher-{DateTime.Now:yyyyMMdd}.log");
                var line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}");
                File.AppendAllText(path, line + Environment.NewLine);
                if (exception is not null)
                {
                    File.AppendAllText(path, exception + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never take down the launcher.
            }
        }
    }
}
