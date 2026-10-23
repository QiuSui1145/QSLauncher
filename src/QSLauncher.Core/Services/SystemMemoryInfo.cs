using System.Runtime.InteropServices;

namespace QSLauncher.Core.Services;

/// <summary>
/// Reads the physical memory that is currently available to the current user.
/// The launcher uses this to make the automatic Java heap recommendation much
/// more useful than a fixed "one size fits all" value.
/// </summary>
public static class SystemMemoryInfo
{
    private const int Megabyte = 1024 * 1024;

    public static MemorySnapshot GetSnapshot()
    {
        try
        {
            var status = new MemoryStatusEx
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };
            if (GlobalMemoryStatusEx(ref status) && status.TotalPhys > 0)
            {
                return new MemorySnapshot(
                    ToMegabytes(status.TotalPhys),
                    ToMegabytes(status.AvailPhys));
            }
        }
        catch
        {
            // Fall through to the portable conservative fallback.
        }

        return new MemorySnapshot(8192, 4096);
    }

    private static int ToMegabytes(ulong bytes)
        => (int)Math.Clamp(bytes / Megabyte, 0, int.MaxValue);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}

public sealed record MemorySnapshot(int TotalMb, int AvailableMb);
