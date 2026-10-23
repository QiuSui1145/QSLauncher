# Builds a pristine demo workspace for screenshots and videos.
#
# The launcher is fully portable: everything it owns lives in `data/` beside
# the executable. This script therefore recreates `artifacts/demo/` from
# scratch, so the demo never contains a real account, a real .minecraft mount,
# a real log or any other user data. Every instance, mod file and wallpaper in
# here is a placeholder that this script generates itself.
param(
    [string]$Version = "1.2.0",
    [string]$Source = "",
    [switch]$Light,
    [string]$Accent = "#4ADE80",
    [double]$GlassOpacity = 0.72,
    [double]$GlassBlur = 18,
    [switch]$KeepData
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Join-Path $root "artifacts\publish\single-$Version\QSLauncher.exe"
}
if (-not (Test-Path -LiteralPath $Source)) {
    throw "找不到启动器发布件：$Source（先运行 tools\publish-release.ps1）"
}

Add-Type -AssemblyName System.Drawing

$demo = Join-Path $root "artifacts\demo"

function Stop-DemoLauncher {
    Get-Process QSLauncher -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($demo, [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force
    Start-Sleep -Milliseconds 400
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

if (-not $KeepData) {
    Stop-DemoLauncher
    if (Test-Path -LiteralPath $demo) {
        Remove-Item -LiteralPath $demo -Recurse -Force
    }
}

$data = Join-Path $demo "data"
foreach ($relative in @(
        "", "backgrounds", "themes", "instances", "meta", "meta\versions",
        "meta\libraries", "meta\assets", "meta\java", "meta\authlib-injector",
        "cache", "downloads", "logs", "quarantine")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $data $relative) | Out-Null
}

Copy-Item -LiteralPath $Source -Destination (Join-Path $demo "QSLauncher.exe") -Force

# ---------------------------------------------------------------- wallpaper --
function New-DemoWallpaper {
    param([string]$Path)

    $width = 2560
    $height = 1600
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

    $rect = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
    $base = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 10, 16, 30),
        [System.Drawing.Color]::FromArgb(255, 16, 56, 60),
        58.0)
    $graphics.FillRectangle($base, $rect)
    $base.Dispose()

    # Soft aurora blobs, drawn as alpha-faded ellipse rings so the result stays
    # smooth without any blur dependency.
    $blobs = @(
        @{ X = 0.22; Y = 0.30; R = 0.44; Rgb = @(74, 222, 128); A = 120 },
        @{ X = 0.76; Y = 0.22; R = 0.38; Rgb = @(56, 189, 248); A = 104 },
        @{ X = 0.52; Y = 0.80; R = 0.50; Rgb = @(129, 140, 248); A = 96 },
        @{ X = 0.08; Y = 0.84; R = 0.32; Rgb = @(45, 212, 191); A = 88 }
    )
    foreach ($blob in $blobs) {
        $rgb = $blob.Rgb
        $radius = $blob.R * $width
        $cx = $blob.X * $width
        $cy = $blob.Y * $height
        $steps = 220
        for ($step = $steps; $step -ge 1; $step--) {
            $t = $step / $steps
            $r = $radius * $t
            # Smoothstep falloff: dense centre, feather-soft rim, no banding.
            $falloff = 1.0 - ($t * $t * (3.0 - 2.0 * $t))
            $alpha = [int]($blob.A * $falloff * 0.34)
            if ($alpha -le 0) { continue }
            $color = [System.Drawing.Color]::FromArgb($alpha, $rgb[0], $rgb[1], $rgb[2])
            $brush = New-Object System.Drawing.SolidBrush($color)
            $graphics.FillEllipse(
                $brush,
                [single]($cx - $r), [single]($cy - $r),
                [single]($r * 2), [single]($r * 2))
            $brush.Dispose()
        }
    }

    # Sparse star field for depth.
    $random = New-Object System.Random(20261022)
    for ($i = 0; $i -lt 1100; $i++) {
        $x = $random.NextDouble() * $width
        $y = $random.NextDouble() * $height * 0.75
        $size = 1.0 + $random.NextDouble() * 1.9
        $alpha = 26 + [int]($random.NextDouble() * 130)
        $star = New-Object System.Drawing.SolidBrush(
            [System.Drawing.Color]::FromArgb($alpha, 236, 246, 255))
        $graphics.FillEllipse($star, [single]$x, [single]$y, [single]$size, [single]$size)
        $star.Dispose()
    }

    # Edge darkening keeps window chrome and white glass readable.
    $bottom = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(0, 4, 8, 16),
        [System.Drawing.Color]::FromArgb(200, 4, 8, 16),
        90.0)
    $graphics.FillRectangle($bottom, $rect)
    $bottom.Dispose()

    $side = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
    $horizontal = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $side,
        [System.Drawing.Color]::FromArgb(150, 3, 6, 12),
        [System.Drawing.Color]::FromArgb(150, 3, 6, 12),
        0.0)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend(3)
    $blend.Colors = @(
        [System.Drawing.Color]::FromArgb(150, 3, 6, 12),
        [System.Drawing.Color]::FromArgb(0, 3, 6, 12),
        [System.Drawing.Color]::FromArgb(150, 3, 6, 12))
    $blend.Positions = @([single]0.0, [single]0.5, [single]1.0)
    $horizontal.InterpolationColors = $blend
    $graphics.FillRectangle($horizontal, $side)
    $horizontal.Dispose()

    $graphics.Dispose()
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

$wallpaperName = "demo-wallpaper.png"
New-DemoWallpaper -Path (Join-Path $data "backgrounds\$wallpaperName")

# ------------------------------------------------------------------ mods -----
# Placeholder jars only: ModService falls back to the file name, so the mod
# list renders without any real download or third-party binary.
function New-DemoMods {
    param(
        [string]$InstanceDirectory,
        [string[]]$Names,
        [string[]]$Disabled = @()
    )

    $mods = Join-Path $InstanceDirectory "game\mods"
    New-Item -ItemType Directory -Force -Path $mods | Out-Null
    foreach ($name in $Names) {
        $file = Join-Path $mods "$name.jar"
        $filler = "QS Launcher demo placeholder. "
        $body = $filler * (18 + ($name.Length % 40))
        Write-Utf8NoBom -Path $file -Text $body
    }
    foreach ($name in $Disabled) {
        $file = Join-Path $mods "$name.jar.disabled"
        Write-Utf8NoBom -Path $file -Text ("QS Launcher demo placeholder. " * 20)
    }
}

function New-DemoInstance {
    param(
        [string]$Id,
        [string]$Name,
        [string]$MinecraftVersion,
        [string]$Loader,
        [string]$LoaderVersion,
        [int]$MemoryMb,
        [datetime]$LastPlayed,
        [datetime]$Created,
        [string]$ModpackName
    )

    $directory = Join-Path $data "instances\$Id"
    foreach ($child in @("", "game", "natives", "mods")) {
        New-Item -ItemType Directory -Force -Path (Join-Path $directory $child) | Out-Null
    }
    foreach ($folder in @("resourcepacks", "shaderpacks", "saves", "config")) {
        New-Item -ItemType Directory -Force -Path (Join-Path $directory "game\$folder") | Out-Null
    }

    $versionId = if ($Loader -eq "Vanilla") {
        $MinecraftVersion
    } else {
        "$MinecraftVersion-$($Loader.ToLowerInvariant())-$LoaderVersion"
    }

    $manifest = [ordered]@{
        id               = $Id
        name             = $Name
        versionId        = $versionId
        minecraftVersion = $MinecraftVersion
        loader           = $Loader
        loaderVersion    = $(if ($Loader -eq "Vanilla") { $null } else { $LoaderVersion })
        createdAt        = $Created.ToString("o")
        lastPlayedAt     = $LastPlayed.ToString("o")
        javaPath         = $null
        javaRuntimeId    = $null
        memoryMb         = $MemoryMb
        jvmArgs          = ""
        gameArgs         = ""
        modpackName      = $(if ($ModpackName) { $ModpackName } else { $null })
        modpackVersion   = $null
        modpackProjectId = $null
        iconPath         = $null
        sourceRoot       = $null
        sourceVersionId  = $null
    }
    Write-Utf8NoBom -Path (Join-Path $directory "instance.json") `
        -Text ($manifest | ConvertTo-Json -Depth 4)

    return $directory
}

$now = Get-Date

$survival = New-DemoInstance -Id "survival" -Name "生存世界 1.21.4" `
    -MinecraftVersion "1.21.4" -Loader "Fabric" -LoaderVersion "0.16.9" -MemoryMb 8192 `
    -LastPlayed $now.AddHours(-2) -Created $now.AddDays(-38)
New-DemoMods -InstanceDirectory $survival -Disabled @("optifine-compat-fabric-1.0.0") -Names @(
    "fabric-api-0.119.2+1.21.4", "sodium-fabric-0.6.5+mc1.21.4", "iris-fabric-1.8.8+mc1.21.4",
    "lithium-fabric-0.14.3+mc1.21.4", "modmenu-11.0.3", "ferritecore-7.0.2-fabric",
    "entityculling-fabric-1.7.2", "dynamic-fps-3.9.7", "xaeros-minimap-25.2.0",
    "appleskin-fabric-3.0.6", "cloth-config-17.0.144"
)

$workshop = New-DemoInstance -Id "workshop" -Name "机械工坊 1.20.1" `
    -MinecraftVersion "1.20.1" -Loader "Forge" -LoaderVersion "47.3.22" -MemoryMb 10240 `
    -LastPlayed $now.AddDays(-1) -Created $now.AddDays(-61)
New-DemoMods -InstanceDirectory $workshop -Names @(
    "create-1.20.1-6.0.6", "jei-1.20.1-forge-15.20.0.106", "flywheel-1.20.1-1.0.4",
    "curios-forge-5.11.1+1.20.1", "patchouli-1.20.1-84-forge"
)

$vanilla = New-DemoInstance -Id "vanilla" -Name "原版纯净 1.21.4" `
    -MinecraftVersion "1.21.4" -Loader "Vanilla" -LoaderVersion "" -MemoryMb 4096 `
    -LastPlayed $now.AddDays(-3) -Created $now.AddDays(-24)

$shader = New-DemoInstance -Id "shader" -Name "光影测试 1.21.1" `
    -MinecraftVersion "1.21.1" -Loader "NeoForge" -LoaderVersion "21.1.228" -MemoryMb 6144 `
    -LastPlayed $now.AddDays(-5) -Created $now.AddDays(-19)
New-DemoMods -InstanceDirectory $shader -Names @(
    "sodium-neoforge-0.6.5+mc1.21.1", "iris-neoforge-1.8.8+mc1.21.1",
    "jei-1.21.1-neoforge-19.21.0.247")

$legacy = New-DemoInstance -Id "legacy" -Name "怀旧整合 1.12.2" `
    -MinecraftVersion "1.12.2" -Loader "Forge" -LoaderVersion "14.23.5.2860" -MemoryMb 5120 `
    -LastPlayed $now.AddDays(-9) -Created $now.AddDays(-96) -ModpackName "怀旧整合包"
New-DemoMods -InstanceDirectory $legacy -Names @(
    "twilightforest-1.12.2-3.11.1021", "jei_1.12.2-4.16.1.1013", "journeymap-1.12.2-5.7.1")

$server = New-DemoInstance -Id "server" -Name "服务器客户端 1.20.1" `
    -MinecraftVersion "1.20.1" -Loader "Fabric" -LoaderVersion "0.15.11" -MemoryMb 4096 `
    -LastPlayed $now.AddDays(-14) -Created $now.AddDays(-44)
New-DemoMods -InstanceDirectory $server -Names @(
    "fabric-api-0.92.2+1.20.1", "sodium-fabric-0.5.11+mc1.20.1", "modmenu-7.2.2")

# ----------------------------------------------------------- saved content ---
foreach ($pack in @("Stay True 1.21.zip", "Fresh Animations 1.9.zip")) {
    Write-Utf8NoBom -Path (Join-Path $survival "game\resourcepacks\$pack") -Text ("demo pack. " * 40)
}
foreach ($shaderPack in @("Complementary Reimagined.zip", "BSL Shaders.zip")) {
    Write-Utf8NoBom -Path (Join-Path $shader "game\shaderpacks\$shaderPack") -Text ("demo shader. " * 40)
}
foreach ($world in @("新的世界", "创造试验场")) {
    $level = Join-Path $survival "game\saves\$world"
    New-Item -ItemType Directory -Force -Path $level | Out-Null
    Write-Utf8NoBom -Path (Join-Path $level "level.dat") -Text "demo world"
}

# -------------------------------------------------------------- settings -----
$themeMode = if ($Light) { "light" } else { "dark" }
$settings = [ordered]@{
    schemaVersion = 1
    appearance    = [ordered]@{
        themeMode   = $themeMode
        accentColor = $Accent
        logoPath    = $null
        background  = [ordered]@{
            mode              = "image"
            path              = "backgrounds/$wallpaperName"
            fit               = "fill"
            dim               = $(if ($Light) { 0.45 } else { 0.28 })
            blur              = 0
            muted             = $true
            loop              = $true
            pauseWhenInactive = $true
        }
        glass       = [ordered]@{
            opacity = $GlassOpacity
            blur    = $GlassBlur
            noise   = 0.06
        }
    }
    accounts      = [ordered]@{ lastAccountId = $null }
    downloads     = [ordered]@{ concurrency = 8; timeoutSeconds = 30; useSystemProxy = $true }
    java          = [ordered]@{ defaultRuntimeId = $null; customJavaPath = $null }
    microsoft     = [ordered]@{ clientId = "" }
    game          = [ordered]@{ roots = @(); mountMode = "single"; activeRoot = $null }
    home          = [ordered]@{
        widgets = @(
            [ordered]@{ key = "launch"; x = 0; y = 0; width = 600; height = 250; visible = $true },
            [ordered]@{ key = "quick"; x = 612; y = 0; width = 268; height = 250; visible = $true },
            [ordered]@{ key = "stats"; x = 0; y = 250; width = 880; height = 100; visible = $true },
            [ordered]@{ key = "recent"; x = 0; y = 360; width = 880; height = 220; visible = $true }
        )
    }
    window        = [ordered]@{
        width = 1600; height = 1000; left = 40; top = 40; maximized = $false
    }
    language      = "zh-CN"
}
Write-Utf8NoBom -Path (Join-Path $data "config.json") -Text ($settings | ConvertTo-Json -Depth 8)

Write-Host "演示目录: $demo"
Write-Host "主题: $themeMode · 强调色: $Accent · 玻璃: $GlassOpacity / $GlassBlur"
Write-Host "实例: 6 个 · 壁纸: $wallpaperName"
Write-Host "该目录不含任何真实账户、日志或 .minecraft 挂载。"
