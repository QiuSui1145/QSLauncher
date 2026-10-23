param(
    [string]$Version = "",
    [switch]$SkipTests,
    [switch]$NoDeploy,
    [string]$DeployDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$propsPath = Join-Path $root "Directory.Build.props"
$props = [IO.File]::ReadAllText($propsPath, [Text.Encoding]::UTF8)
$match = [regex]::Match($props, "<Version>(\d+)\.(\d+)\.(\d+)</Version>")
if (-not $match.Success) {
    throw "Directory.Build.props 中没有三段式 Version。"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $major = [int]$match.Groups[1].Value
    $minor = [int]$match.Groups[2].Value
    $patch = [int]$match.Groups[3].Value + 1
    $Version = "$major.$minor.$patch"
}

if ($Version -notmatch "^\d+\.\d+\.\d+$") {
    throw "版本号必须形如 1.2.3。"
}

$updated = [regex]::Replace(
    $props,
    "<Version>\d+\.\d+\.\d+</Version>",
    "<Version>$Version</Version>")
[IO.File]::WriteAllText($propsPath, $updated, (New-Object Text.UTF8Encoding($false)))
Write-Host "版本号: $Version"

Push-Location $root
try {
    if (-not $SkipTests) {
        & dotnet test QSLauncher.slnx -c Release -v:minimal --nologo
        if ($LASTEXITCODE -ne 0) { throw "测试失败。" }
    }

    $singleDir = Join-Path $root "artifacts\publish\single-$Version"
    $liteDir = Join-Path $root "artifacts\publish\lite-$Version"

    & dotnet publish "src\QSLauncher.App\QSLauncher.App.csproj" `
        -c Release -p:RuntimeIdentifier=win-x64 -p:SelfContained=true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false `
        -p:DebugType=None -p:DebugSymbols=false -o $singleDir -v:minimal --nologo
    if ($LASTEXITCODE -ne 0) { throw "自包含版发布失败。" }

    & dotnet publish "src\QSLauncher.App\QSLauncher.App.csproj" `
        -c Release -p:RuntimeIdentifier=win-x64 -p:SelfContained=false `
        -p:PublishSingleFile=true -p:PublishReadyToRun=false `
        -p:DebugType=None -p:DebugSymbols=false -o $liteDir -v:minimal --nologo
    if ($LASTEXITCODE -ne 0) { throw "轻量版发布失败。" }

    $single = Join-Path $singleDir "QSLauncher.exe"
    $lite = Join-Path $liteDir "QSLauncher.exe"
    if (-not (Test-Path -LiteralPath $single)) { throw "缺少 $single" }
    if (-not (Test-Path -LiteralPath $lite)) { throw "缺少 $lite" }

    if (-not $NoDeploy -and -not [string]::IsNullOrWhiteSpace($DeployDir)) {
        if (-not (Test-Path -LiteralPath $DeployDir)) {
            throw "部署目录不存在：$DeployDir"
        }
        Copy-Item -LiteralPath $single -Destination (Join-Path $DeployDir "QSLauncher-$Version-win-x64.exe") -Force
        Copy-Item -LiteralPath $lite -Destination (Join-Path $DeployDir "QSLauncher-$Version-lite.exe") -Force
        Write-Host "已部署到 $DeployDir"
    }

    Write-Host "单文件: $single"
    Write-Host ("  {0:N0} bytes  SHA256 {1}" -f (Get-Item $single).Length, (Get-FileHash $single -Algorithm SHA256).Hash)
    Write-Host "轻量版: $lite"
    Write-Host ("  {0:N0} bytes  SHA256 {1}" -f (Get-Item $lite).Length, (Get-FileHash $lite -Algorithm SHA256).Hash)
}
finally {
    Pop-Location
}
