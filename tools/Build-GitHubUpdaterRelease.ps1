param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository
)

$ErrorActionPreference = 'Stop'

$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$guiProject = Join-Path $workspace 'launcher\KrDalamudUpdaterGui\KrDalamudUpdaterGui.csproj'
$bootstrapSource = Join-Path $workspace 'launcher\KrDalamudUpdaterBootstrap\Program.cs'
$bootstrapCompiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$launcherIcon = Join-Path $workspace 'assets\launcher-icon\launcher-icon.ico'
$readme = Join-Path $workspace 'launcher\KrDalamudUpdaterBootstrap\README-KR.txt'
$licensesRoot = Join-Path $workspace 'licenses'
$stageRoot = Join-Path $workspace 'artifacts\github-release\payload'
$bootstrapRoot = Join-Path $workspace 'artifacts\github-release\bootstrap'
$releaseRoot = Join-Path $workspace 'dist\github-release'
$portableRoot = Join-Path $workspace "artifacts\github-release\KR-Dalamud-Updater-$Version-Portable"
$payloadZip = Join-Path $releaseRoot 'KR-Dalamud-Updater-Payload.zip'
$portableZip = Join-Path $releaseRoot "KR-Dalamud-Updater-$Version-Portable.zip"

function Assert-WorkspacePath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = $workspace.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the workspace: $fullPath"
    }
}

foreach ($path in @($stageRoot, $bootstrapRoot, $releaseRoot, $portableRoot, $payloadZip, $portableZip)) {
    Assert-WorkspacePath $path
}

foreach ($path in @($stageRoot, $bootstrapRoot, $releaseRoot, $portableRoot)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $stageRoot, $bootstrapRoot, $releaseRoot, $portableRoot -Force | Out-Null

dotnet restore $guiProject -r win-x64
if ($LASTEXITCODE -ne 0) {
    throw "GUI restore failed with exit code $LASTEXITCODE."
}

dotnet publish $guiProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    --no-restore `
    -p:Version=$Version `
    -p:SelfContained=false `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $stageRoot
if ($LASTEXITCODE -ne 0) {
    throw "GUI publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $stageRoot 'KrDalamudUpdaterGui.exe'
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Published GUI executable was not found: $publishedExe"
}

Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $stageRoot 'Dalamud.Updater.Gui.exe') -Force
Remove-Item -LiteralPath $publishedExe -Force
Remove-Item -LiteralPath (Join-Path $stageRoot 'DalamudUpdaterConfig.json') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $stageRoot 'KrDalamudUpdaterGui.pdb') -Force -ErrorAction SilentlyContinue

$publishedLicensesRoot = Join-Path $stageRoot 'licenses'
New-Item -ItemType Directory -Path $publishedLicensesRoot -Force | Out-Null
Copy-Item -Path (Join-Path $licensesRoot '*') -Destination $publishedLicensesRoot -Recurse -Force

Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $payloadZip -CompressionLevel Optimal

if (-not (Test-Path -LiteralPath $bootstrapCompiler -PathType Leaf)) {
    throw "The .NET Framework compiler was not found: $bootstrapCompiler"
}

$bootstrapExe = Join-Path $bootstrapRoot 'Dalamud.Updater.exe'
Push-Location $workspace
try {
    & $bootstrapCompiler `
        /nologo `
        /target:winexe `
        /platform:anycpu `
        /optimize+ `
        "/out:$bootstrapExe" `
        "/win32icon:$launcherIcon" `
        "/resource:$payloadZip,KrDalamudUpdaterBootstrap.Payload.KrDalamudUpdaterGui.zip" `
        /reference:System.dll `
        /reference:System.Core.dll `
        /reference:System.Windows.Forms.dll `
        /reference:System.Web.Extensions.dll `
        /reference:System.IO.Compression.dll `
        /reference:System.IO.Compression.FileSystem.dll `
        $bootstrapSource
    if ($LASTEXITCODE -ne 0) {
        throw "Bootstrap compilation failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Copy-Item -LiteralPath $bootstrapExe -Destination $portableRoot -Force
Copy-Item -LiteralPath $readme -Destination $portableRoot -Force

$releaseConfig = [ordered]@{
    Enabled = $true
    Repository = $Repository
    AssetName = 'KR-Dalamud-Updater-Payload.zip'
    TimeoutSeconds = 20
}
$releaseConfig | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $portableRoot 'UpdaterReleaseConfig.json') -Encoding UTF8

Compress-Archive -Path (Join-Path $portableRoot '*') -DestinationPath $portableZip -CompressionLevel Optimal

$payloadHash = (Get-FileHash -LiteralPath $payloadZip -Algorithm SHA256).Hash
$portableHash = (Get-FileHash -LiteralPath $portableZip -Algorithm SHA256).Hash

[pscustomobject]@{
    Version = $Version
    Repository = $Repository
    Payload = $payloadZip
    PayloadSha256 = $payloadHash
    Portable = $portableZip
    PortableSha256 = $portableHash
}
