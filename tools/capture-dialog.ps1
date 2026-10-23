param(
    [string]$ProcessName = "QSLauncher",
    [string]$TitleLike = "",
    [string]$Output = "artifacts/dialog.png",
    [int]$WaitSeconds = 1
)

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class QSDialogCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    public static List<IntPtr> Windows(uint pid, string titleLike) {
        var result = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) return true;
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrEmpty(titleLike) || title.IndexOf(titleLike, StringComparison.OrdinalIgnoreCase) >= 0) {
                result.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
    public static string Title(IntPtr hWnd) {
        var sb = new StringBuilder(512);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
"@
[QSDialogCapture]::SetProcessDPIAware() | Out-Null
Add-Type -AssemblyName System.Drawing

Start-Sleep -Seconds $WaitSeconds
$process = Get-Process $ProcessName -ErrorAction Stop | Select-Object -First 1
$windows = [QSDialogCapture]::Windows([uint32]$process.Id, $TitleLike)
if ($windows.Count -eq 0) {
    throw "No visible window matching '$TitleLike' for $ProcessName"
}

$handle = $windows[0]
$rect = New-Object QSDialogCapture+RECT
[QSDialogCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    throw "Window has empty bounds: $handle"
}

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
$ok = [QSDialogCapture]::PrintWindow($handle, $hdc, 2)
$graphics.ReleaseHdc($hdc)
$graphics.Dispose()

$directory = Split-Path -Parent $Output
if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
$bitmap.Save((Resolve-Path -LiteralPath (Split-Path -Parent $Output)).Path + "\" + (Split-Path -Leaf $Output), [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
"CAPTURED=${width}x${height} OK=$ok TITLE=$([QSDialogCapture]::Title($handle)) PATH=$Output"
