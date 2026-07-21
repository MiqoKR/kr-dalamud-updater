param(
    [string]$Version = "0.4.4"
)

$ErrorActionPreference = "Stop"

$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$guiProject = Join-Path $workspace "launcher\KrDalamudUpdaterGui\KrDalamudUpdaterGui.csproj"
$bootstrapSource = Join-Path $workspace "launcher\KrDalamudUpdaterBootstrap\Program.cs"
$bootstrapCompiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$launcherIcon = Join-Path $workspace "assets\launcher-icon\launcher-icon.ico"
$payloadRoot = Join-Path $workspace "launcher\KrDalamudUpdaterBootstrap\Payload"
$payloadZip = Join-Path $payloadRoot "KrDalamudUpdaterGui.zip"
$stageRoot = Join-Path $workspace "artifacts\KrDalamudUpdaterGui-$Version"
$bootstrapOut = Join-Path $workspace "artifacts\KrDalamudUpdaterBootstrap-$Version"
$distRoot = Join-Path $workspace "dist\KR-Dalamud-Updater-$Version-Portable"
$distZip = Join-Path $workspace "dist\KR-Dalamud-Updater-$Version-Portable.zip"

function Assert-WorkspacePath([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $prefix = $workspace.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the workspace: $fullPath"
    }
}

foreach ($path in @($payloadRoot, $stageRoot, $bootstrapOut, $distRoot, $distZip)) {
    Assert-WorkspacePath $path
}

foreach ($path in @($stageRoot, $bootstrapOut, $distRoot)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

if (Test-Path -LiteralPath $distZip) {
    Remove-Item -LiteralPath $distZip -Force
}

New-Item -ItemType Directory -Path $payloadRoot, $stageRoot, $bootstrapOut, $distRoot -Force | Out-Null

dotnet publish $guiProject -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false --no-restore -o $stageRoot
if ($LASTEXITCODE -ne 0) {
    throw "GUI publish failed with exit code $LASTEXITCODE."
}

Remove-Item -LiteralPath (Join-Path $stageRoot "KrDalamudUpdaterGui.pdb") -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $stageRoot "KrDalamudUpdaterGui.exe") -Destination (Join-Path $stageRoot "Dalamud.Updater.Gui.exe") -Force
Remove-Item -LiteralPath (Join-Path $stageRoot "KrDalamudUpdaterGui.exe") -Force

if (Test-Path -LiteralPath $payloadZip) {
    Remove-Item -LiteralPath $payloadZip -Force
}
Compress-Archive -Path (Join-Path $stageRoot "*") -DestinationPath $payloadZip -CompressionLevel Optimal

if (-not (Test-Path -LiteralPath $bootstrapCompiler)) {
    throw "The .NET Framework C# compiler was not found: $bootstrapCompiler"
}

$bootstrapExe = Join-Path $bootstrapOut "Dalamud.Updater.exe"
$bootstrapExeRelative = "artifacts\KrDalamudUpdaterBootstrap-$Version\Dalamud.Updater.exe"
$bootstrapSourceRelative = "launcher\KrDalamudUpdaterBootstrap\Program.cs"
$payloadZipRelative = "launcher\KrDalamudUpdaterBootstrap\Payload\KrDalamudUpdaterGui.zip"
$launcherIconRelative = "assets\launcher-icon\launcher-icon.ico"
Push-Location $workspace
try {
    & $bootstrapCompiler /nologo /target:winexe /platform:anycpu /optimize+ /out:$bootstrapExeRelative /win32icon:$launcherIconRelative /resource:$payloadZipRelative,KrDalamudUpdaterBootstrap.Payload.KrDalamudUpdaterGui.zip /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $bootstrapSourceRelative
    if ($LASTEXITCODE -ne 0) {
        throw "Bootstrap compilation failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Copy-Item -LiteralPath $bootstrapExe -Destination $distRoot -Force
Copy-Item -LiteralPath (Join-Path $workspace "launcher\KrDalamudUpdaterBootstrap\README-KR.txt") -Destination $distRoot -Force
Compress-Archive -Path (Join-Path $distRoot "*") -DestinationPath $distZip -CompressionLevel Optimal

$exe = Join-Path $distRoot "Dalamud.Updater.exe"
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$zipHash = (Get-FileHash -LiteralPath $distZip -Algorithm SHA256).Hash

[pscustomobject]@{
    Version = $Version
    Executable = $exe
    ExecutableBytes = (Get-Item -LiteralPath $exe).Length
    ExecutableSha256 = $hash
    Zip = $distZip
    ZipBytes = (Get-Item -LiteralPath $distZip).Length
    ZipSha256 = $zipHash
}
