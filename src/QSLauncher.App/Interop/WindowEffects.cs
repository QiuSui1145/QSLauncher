using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QSLauncher.App.Interop;

public static class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmcpRound = 2;
    private const int DwmsbtMainWindow = 2;
    private const int DwmsbtNone = 1;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int valueSize);

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static bool IsMicaAvailable => Environment.OSVersion.Version.Build >= 22621;

    public static void Apply(Window window, bool dark, bool enableMica)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        TrySet(handle, DwmwaUseImmersiveDarkMode, dark ? 1 : 0);
        TrySet(handle, DwmwaUseImmersiveDarkModeLegacy, dark ? 1 : 0);

        if (IsWindows11)
        {
            TrySet(handle, DwmwaWindowCornerPreference, DwmcpRound);
            if (IsMicaAvailable)
            {
                TrySet(handle, DwmwaSystemBackdropType, enableMica ? DwmsbtMainWindow : DwmsbtNone);
            }
        }
    }

    private static void TrySet(IntPtr handle, int attribute, int value)
    {
        try
        {
            _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch
        {
            // Older Windows versions simply ignore unsupported attributes.
        }
    }
}
