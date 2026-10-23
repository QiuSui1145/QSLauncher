using System.Text.RegularExpressions;

namespace QSLauncher.Core.Services;

public enum LogIssueSeverity
{
    Info,
    Warning,
    Error
}

public sealed record LogFinding(
    LogIssueSeverity Severity,
    string Title,
    string Detail,
    string Suggestion,
    string? ModName = null);

/// <summary>
/// Turns the most common Minecraft / mod-loader failure signatures into
/// actionable Chinese guidance. This is intentionally rule based: it never
/// uploads a log and never invents a diagnosis it cannot see in the text.
/// </summary>
public static partial class LogAnalyzer
{
    private static readonly Regex QuotedModPattern = new(
        @"[\u0022'](?<name>[a-z0-9_.-]{3,64})[\u0022']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ModIdPattern = new(
        @"(?:mod|modid|mod id|模组)\s*[:=]?\s*(?<name>[a-z0-9_.-]{3,64})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static LogFinding? Analyze(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        if (line.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Java heap space", StringComparison.OrdinalIgnoreCase)
            || line.Contains("GC overhead limit exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return new LogFinding(
                LogIssueSeverity.Error,
                "内存不足",
                "Java 堆内存已经耗尽，游戏无法继续加载。",
                "在实例设置中使用“智能分配内存”，或手动增加 1024–2048 MB；同时检查是否安装了重复或过大的整合包。");
        }

        if (line.Contains("DuplicateModsFoundException", StringComparison.OrdinalIgnoreCase)
            || (line.Contains("duplicate mod", StringComparison.OrdinalIgnoreCase)
                && line.Contains("found", StringComparison.OrdinalIgnoreCase)))
        {
            return new LogFinding(
                LogIssueSeverity.Error,
                "发现重复模组",
                "同一个模组 ID 被加载了两次，常见于同时放入多个版本或整合包自带的重复 JAR。",
                "打开实例的 mods 文件夹，删除重复的 JAR；保留与当前游戏版本、加载器匹配的那一个。",
                ExtractModName(line));
        }

        if (line.Contains("Incompatible mod set", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Incompatible mods found", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Mod resolution failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Missing or unsupported mandatory dependencies", StringComparison.OrdinalIgnoreCase))
        {
            return new LogFinding(
                LogIssueSeverity.Error,
                "模组依赖或版本不兼容",
                "加载器拒绝了当前模组组合，通常缺少前置模组，或模组版本与 Minecraft / 加载器不匹配。",
                "按日志中的模组名补齐前置模组；确认所有模组都支持当前 Minecraft 版本和加载器，再逐个禁用最近安装的模组排查。",
                ExtractModName(line));
        }

        if (line.Contains("Mixin apply failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Mixin transformation failed", StringComparison.OrdinalIgnoreCase))
        {
            return new LogFinding(
                LogIssueSeverity.Error,
                "Mixin 注入失败",
                "某个模组试图修改游戏代码时失败，常见原因是模组与游戏版本不匹配或与其他模组冲突。",
                "优先更新日志中提到的模组；如果刚安装新模组后才出现，先禁用该模组再启动。",
                ExtractModName(line));
        }

        if (line.Contains("UnsupportedClassVersionError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("has been compiled by a more recent version of the Java Runtime",
                StringComparison.OrdinalIgnoreCase))
        {
            return new LogFinding(
                LogIssueSeverity.Error,
                "Java 版本不匹配",
                "模组或游戏使用了更高版本的 Java 编译，当前 Java 运行时太旧。",
                "在 Java 页面安装该游戏版本要求的 Java（常见为 Java 21 或 25），然后在实例设置中指定它。");
        }

        if (line.Contains("java.lang.NoClassDefFoundError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ClassNotFoundException", StringComparison.OrdinalIgnoreCase))
        {
            return new LogFinding(
                LogIssueSeverity.Error,
                "缺少运行库或模组前置",
                "游戏找不到某个类，可能是依赖库缺失、模组前置未安装，或 JAR 文件损坏。",
                "重新校验版本文件；检查模组前置是否齐全，并删除下载不完整的 JAR 后重新安装。",
                ExtractModName(line));
        }

        if (line.Contains("Address already in use", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Failed to bind to port", StringComparison.OrdinalIgnoreCase))
        {
            return new LogFinding(
                LogIssueSeverity.Warning,
                "端口被占用",
                "游戏需要的网络端口已被其他程序占用。",
                "关闭重复启动的游戏实例或其他占用端口的程序，然后重试。");
        }

        if (line.Contains("Could not reserve enough space", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Invalid maximum heap size", StringComparison.OrdinalIgnoreCase))
        {
            return new LogFinding(
                LogIssueSeverity.Error,
                "内存参数无效",
                "分配的内存超过了系统或 Java 能提供的范围。",
                "降低实例内存，或安装 64 位 Java；32 位 Java 通常无法使用超过 1–1.5 GB 的堆。");
        }

        if (line.Contains("GLFW error", StringComparison.OrdinalIgnoreCase)
            && line.Contains("Failed to create window", StringComparison.OrdinalIgnoreCase))
        {
            return new LogFinding(
                LogIssueSeverity.Warning,
                "图形窗口创建失败",
                "显卡驱动或远程桌面环境阻止了游戏创建窗口。",
                "更新显卡驱动，避免在远程桌面/虚拟机中启动，或尝试关闭光影与高分辨率资源包。");
        }

        return null;
    }

    private static string? ExtractModName(string line)
    {
        var quoted = QuotedModPattern.Match(line);
        if (quoted.Success)
        {
            return quoted.Groups["name"].Value;
        }

        var id = ModIdPattern.Match(line);
        return id.Success ? id.Groups["name"].Value : null;
    }
}
