using QSLauncher.Core.Models;

namespace QSLauncher.Core.Services;

/// <summary>
/// Picks an aggressive but still safe Java heap size. The instance content
/// supplies a floor, while the host's currently available physical memory
/// supplies the real upper bound. This avoids the old fixed 4 GB ceiling on
/// machines with plenty of free RAM.
/// </summary>
public static class MemoryAdvisor
{
    private const int MegabyteStep = 256;
    private const int MaximumHeapMb = 32768;

    public static MemoryRecommendation Recommend(
        InstanceInfo instance,
        MemorySnapshot? memory = null)
    {
        var mods = CountFiles(instance.ModsDirectory, "*.jar", "*.jar.disabled");
        var resourcePacks = CountEntries(Path.Combine(instance.GameDirectory, "resourcepacks"));
        var shaderPacks = CountEntries(Path.Combine(instance.GameDirectory, "shaderpacks"));
        var modded = instance.Manifest.Loader != LoaderKind.Vanilla || mods > 0;

        var contentFloor = modded ? 3072 : 2048;
        contentFloor += mods * 80;
        contentFloor += resourcePacks * 48;
        contentFloor += shaderPacks * 128;
        if (mods >= 100)
        {
            contentFloor += 1024;
        }

        if (mods >= 250)
        {
            contentFloor += 2048;
        }

        contentFloor = Math.Clamp(contentFloor, 2048, MaximumHeapMb);

        var snapshot = memory ?? SystemMemoryInfo.GetSnapshot();
        var available = Math.Max(0, snapshot.AvailableMb);
        var total = Math.Max(0, snapshot.TotalMb);

        // Reserve a small slice for Windows and background apps. On a machine
        // with 32 GB free this still recommends close to 28 GB, while a low
        // memory machine gets a sensible defensive cap.
        var reserve = Math.Clamp(available / 8, 1024, 4096);
        var freeBudget = Math.Max(2048, available - reserve);
        var hardCeiling = Math.Min(
            MaximumHeapMb,
            Math.Max(2048, Math.Min(available - 512, total - 2048)));

        var megabytes = Math.Min(Math.Max(contentFloor, freeBudget), hardCeiling);
        megabytes = Math.Max(2048, megabytes);
        megabytes = (int)Math.Floor(megabytes / (double)MegabyteStep) * MegabyteStep;
        megabytes = Math.Clamp(megabytes, 2048, hardCeiling);

        var content = mods == 0 && resourcePacks == 0 && shaderPacks == 0
            ? "原版 / 无额外内容"
            : $"{mods} 个模组 · {resourcePacks} 个资源包 · {shaderPacks} 个光影";
        var reason =
            $"{content}；本机剩余 {available} MB，按可用内存上限分配 {megabytes} MB" +
            (total > 0 ? $"（物理内存 {total} MB）" : string.Empty);
        return new MemoryRecommendation(megabytes, mods, resourcePacks, shaderPacks, reason);
    }

    private static int CountFiles(string directory, params string[] patterns)
    {
        try
        {
            return Directory.Exists(directory)
                ? patterns.Sum(pattern => Directory.GetFiles(directory, pattern).Length)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static int CountEntries(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            return Directory.GetFiles(directory).Length + Directory.GetDirectories(directory).Length;
        }
        catch
        {
            return 0;
        }
    }
}

public sealed record MemoryRecommendation(
    int MemoryMb,
    int ModCount,
    int ResourcePackCount,
    int ShaderPackCount,
    string Reason);
