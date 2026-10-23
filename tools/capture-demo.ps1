# Captures demo screenshots from a running QS Launcher window.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\capture-demo.ps1 -Pages 首页,设置 -OutputDir artifacts\demo\shots -Prefix light
#
# The window is captured with PrintWindow + PW_RENDERFULLCONTENT so the result
# is the real rendered UI, not a screen grab of whatever the desktop shows.
param(
    [string]$ProcessName = "QSLauncher",
    [string[]]$Pages = @(),
    [string]$OutputDir = "artifacts\demo\shots",
    [string]$Prefix = "shot",
    [switch]$IncludeChrome,
    [switch]$AccountMenu,
    [int]$SettleMs = 900,
    [switch]$Pythonless  # reserved: kept so callers can pass flags harmlessly
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root $OutputDir
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class QSDemoCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@
[QSDemoCapture]::SetProcessDPIAware() | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase

$automation = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]

function Get-AppWindow {
    param([IntPtr]$Handle)
    return $automation::FromHandle($Handle)
}

function Invoke-Element {
    param($Element, [string]$Label)

    if ($null -eq $Element) { throw "找不到元素：$Label" }
    $pattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        $pattern.Invoke()
        return
    }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
        $pattern.Select()
        return
    }
    throw "元素不支持点击：$Label"
}

function Find-ByName {
    param($Root, [string]$Name)

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        $automation::NameProperty, $Name)
    return $Root.FindFirst($scope::Descendants, $condition)
}

function Save-WindowShot {
    param([IntPtr]$Handle, [string]$Path)

    $rect = New-Object QSDemoCapture+RECT
    [QSDemoCapture]::GetWindowRect($Handle, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [QSDemoCapture]::PrintWindow($Handle, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return "CAPTURED=${width}x${height} OK=$ok"
}

function Save-ScreenShot {
    param([IntPtr]$Handle, [string]$Path)

    # PrintWindow cannot see a WPF Popup (separate HWND), so grab the window's
    # on-screen rectangle instead. The popup sits inside that rectangle.
    $rect = New-Object QSDemoCapture+RECT
    [QSDemoCapture]::GetWindowRect($Handle, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen(
        (New-Object System.Drawing.Point($rect.Left, $rect.Top)),
        [System.Drawing.Point]::Empty,
        (New-Object System.Drawing.Size($width, $height)))
    $graphics.Dispose()
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return "SCREEN=${width}x$height"
}

$process = Get-Process $ProcessName -ErrorAction Stop | Select-Object -First 1
$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) { throw "进程 $ProcessName 还没有主窗口。" }
[QSDemoCapture]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds $SettleMs

$window = Get-AppWindow -Handle $handle

# `powershell -File` cannot bind a real array, so accept "首页,设置" too.
$pageList = @($Pages | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } |
        ForEach-Object { $_.Trim() })

foreach ($page in $pageList) {
    $safe = $page
    $target = Find-ByName -Root $window -Name $page
    Invoke-Element -Element $target -Label $page
    Start-Sleep -Milliseconds ($SettleMs + 700)
    $path = Join-Path $outDir ("{0}-{1}.png" -f $Prefix, $safe)
    $result = Save-WindowShot -Handle $handle -Path $path
    Write-Host ("PAGE {0} -> {1} {2}" -f $page, $path, $result)
}

if ($AccountMenu) {
    $button = Find-ByName -Root $window -Name "切换账户"
    Invoke-Element -Element $button -Label "切换账户"
    Start-Sleep -Milliseconds 800
    $path = Join-Path $outDir ("{0}-account-menu.png" -f $Prefix)
    $result = Save-ScreenShot -Handle $handle -Path $path
    Write-Host ("ACCOUNT MENU -> {0} {1}" -f $path, $result)
}

if ($IncludeChrome) {
    $path = Join-Path $outDir ("{0}-current.png" -f $Prefix)
    $result = Save-WindowShot -Handle $handle -Path $path
    Write-Host ("WINDOW -> {0} {1}" -f $path, $result)
}

Write-Host "输出目录: $outDir"
