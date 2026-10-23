param(
    [string]$ProcessName = "QSLauncher",
    [string]$Output = "artifacts/window.png",
    [int]$WaitSeconds = 3
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class QSCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint flags);
}
"@
[QSCapture]::SetProcessDPIAware() | Out-Null
Add-Type -AssemblyName System.Drawing

Start-Sleep -Seconds $WaitSeconds
$process = Get-Process $ProcessName -ErrorAction Stop | Select-Object -First 1
$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) {
    throw "No main window handle for $ProcessName"
}

$rect = New-Object QSCapture+RECT
[QSCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
$ok = [QSCapture]::PrintWindow($handle, $hdc, 2)
$graphics.ReleaseHdc($hdc)
$graphics.Dispose()

$directory = Split-Path -Parent $Output
if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
$bitmap.Save((Resolve-Path -LiteralPath (Split-Path -Parent $Output)).Path + "\" + (Split-Path -Leaf $Output), [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
"CAPTURED=${width}x${height} OK=$ok PATH=$Output"
