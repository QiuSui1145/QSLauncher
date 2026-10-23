using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

/// <summary>
/// Owns every path used by the launcher. Nothing is written outside the
/// directory that contains QSLauncher.exe.
/// </summary>
public sealed class PortablePaths
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".webp"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".wmv", ".avi", ".mov", ".m4v"
    };

    public PortablePaths(string? root = null)
    {
        Root = Path.GetFullPath(string.IsNullOrWhiteSpace(root) ? AppContext.BaseDirectory : root);
        DataRoot = Path.Combine(Root, "data");
        ConfigPath = Path.Combine(DataRoot, "config.json");
        ConfigBackupPath = ConfigPath + ".bak";
        BackgroundsPath = Path.Combine(DataRoot, "backgrounds");
        ThemesPath = Path.Combine(DataRoot, "themes");
        InstancesPath = Path.Combine(DataRoot, "instances");
        MetaPath = Path.Combine(DataRoot, "meta");
        VersionsPath = Path.Combine(MetaPath, "versions");
        LibrariesPath = Path.Combine(MetaPath, "libraries");
        AssetsPath = Path.Combine(MetaPath, "assets");
        JavaPath = Path.Combine(MetaPath, "java");
        AuthlibPath = Path.Combine(MetaPath, "authlib-injector");
        CachePath = Path.Combine(DataRoot, "cache");
        DownloadsPath = Path.Combine(DataRoot, "downloads");
        LogsPath = Path.Combine(DataRoot, "logs");
        QuarantinePath = Path.Combine(DataRoot, "quarantine");
    }

    public string Root { get; }

    public string DataRoot { get; }

    public string ConfigPath { get; }

    public string ConfigBackupPath { get; }

    public string BackgroundsPath { get; }

    public string ThemesPath { get; }

    public string InstancesPath { get; }

    public string MetaPath { get; }

    public string VersionsPath { get; }

    public string LibrariesPath { get; }

    public string AssetsPath { get; }

    public string JavaPath { get; }

    public string AuthlibPath { get; }

    public string CachePath { get; }

    public string DownloadsPath { get; }

    public string LogsPath { get; }

    public string QuarantinePath { get; }

    public void EnsureLayout()
    {
        foreach (var path in new[]
                 {
                     DataRoot, BackgroundsPath, ThemesPath, InstancesPath, MetaPath,
                     VersionsPath, LibrariesPath, AssetsPath, JavaPath, AuthlibPath,
                     CachePath, DownloadsPath, LogsPath, QuarantinePath
                 })
        {
            Directory.CreateDirectory(path);
        }

        var probe = Path.Combine(DataRoot, $".write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "qs");
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
    }

    public string ResolveDataPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return DataRoot;
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(DataRoot, normalized));
        var dataRoot = Path.GetFullPath(DataRoot);
        if (!IsWithin(fullPath, dataRoot))
        {
            throw new InvalidOperationException("背景路径必须位于 data 目录内。");
        }

        return fullPath;
    }

    public string ToDataRelativePath(string fullPath)
    {
        var relative = Path.GetRelativePath(DataRoot, fullPath).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("文件不在 data 目录内。");
        }

        return relative;
    }

    public string ImportBackground(string sourcePath, BackgroundMode mode)
    {
        if (mode is not (BackgroundMode.Image or BackgroundMode.Video))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("找不到背景文件。", source);
        }

        var extension = Path.GetExtension(source);
        if (mode == BackgroundMode.Image && !ImageExtensions.Contains(extension))
        {
            throw new InvalidOperationException("图片背景仅支持 PNG、JPG、JPEG、BMP、WEBP。");
        }

        if (mode == BackgroundMode.Video && !VideoExtensions.Contains(extension))
        {
            throw new InvalidOperationException("视频背景仅支持 MP4、WMV、AVI、MOV、M4V。");
        }

        Directory.CreateDirectory(BackgroundsPath);
        if (IsWithin(source, BackgroundsPath))
        {
            return ToDataRelativePath(source);
        }

        var safeExtension = extension.ToLowerInvariant();
        var destination = Path.Combine(
            BackgroundsPath,
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{safeExtension}");

        File.Copy(source, destination, overwrite: false);
        return ToDataRelativePath(destination);
    }

    public string ImportLogo(string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("找不到 Logo 图片。", source);
        }

        var extension = Path.GetExtension(source);
        if (!ImageExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Logo 仅支持 PNG、JPG、JPEG、BMP、WEBP。");
        }

        Directory.CreateDirectory(BackgroundsPath);
        if (IsWithin(source, BackgroundsPath))
        {
            return ToDataRelativePath(source);
        }

        var destination = Path.Combine(
            BackgroundsPath,
            $"logo-{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        File.Copy(source, destination, overwrite: false);
        return ToDataRelativePath(destination);
    }

    public static bool IsSupportedImage(string path)
        => ImageExtensions.Contains(Path.GetExtension(path));

    public static bool IsSupportedVideo(string path)
        => VideoExtensions.Contains(Path.GetExtension(path));

    private static bool IsWithin(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
