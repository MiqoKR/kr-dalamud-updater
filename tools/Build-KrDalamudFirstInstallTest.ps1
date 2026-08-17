[CmdletBinding()]
param(
    [string]$Version = '0.5.0-test.2',
    [string]$ProfileRoot = '%APPDATA%\XIVLauncherKR-FirstInstallTest'
)

$ErrorActionPreference = 'Stop'

$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $workspace 'launcher\KrDalamudUpdaterGui\KrDalamudUpdaterGui.csproj'
$releaseRoot = Join-Path $workspace 'dist\first-install-test'
$packageName = "KR-Dalamud-FirstInstall-Test-$Version"
$stageRoot = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"

function Assert-WorkspacePath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = $workspace.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the workspace: $fullPath"
    }
}

Assert-WorkspacePath $releaseRoot
Assert-WorkspacePath $stageRoot
Assert-WorkspacePath $zipPath

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
New-Item -ItemType Directory -Path $stageRoot | Out-Null

dotnet restore $project -r win-x64
if ($LASTEXITCODE -ne 0) {
    throw "Restore failed with exit code $LASTEXITCODE."
}

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    --no-restore `
    -p:Version=$Version `
    -p:AssemblyVersion=0.5.0.2 `
    -p:FileVersion=0.5.0.2 `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $stageRoot
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $stageRoot 'KrDalamudUpdaterGui.exe'
$testExe = Join-Path $stageRoot 'KR.Dalamud.FirstInstall.Test.exe'
Move-Item -LiteralPath $publishedExe -Destination $testExe -Force
Remove-Item -LiteralPath (Join-Path $stageRoot 'KrDalamudUpdaterGui.pdb') -Force -ErrorAction SilentlyContinue

$config = [ordered]@{
    ProfileRoot = $ProfileRoot
    HookVersion = '15.0.3.2'
    AutoStart = $true
    AutoApply = $true
    DisablePlugins = $true
    DisableCustomRepoPlugins = $true
    DelaySeconds = 1
    InitializeEmptyProfile = $true
    UseSystemDotnet = $true
    RequireIsolatedProfile = $true
    DistributionLabel = 'FIRST INSTALL TEST'
}
$config | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stageRoot 'DalamudUpdaterConfig.json') -Encoding UTF8

@"
KR Dalamud First Install Test $Version

1. Microsoft .NET 10 Desktop Runtime x64가 필요합니다.
2. 기존 메인 프로필과 분리된 다음 경로를 사용합니다.
   $ProfileRoot
3. 게임을 종료한 상태에서 업데이트 확인을 누르세요.
4. 첫 테스트는 플러그인과 커스텀 저장소가 비활성화됩니다.
5. 문제가 생기면 테스트 프로필 폴더만 삭제하면 됩니다.

메인 %APPDATA%\XIVLauncherKR 프로필은 수정하지 않습니다.
"@ | Set-Content -LiteralPath (Join-Path $stageRoot 'README-TEST-KR.txt') -Encoding UTF8

Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

$exeHash = (Get-FileHash -LiteralPath $testExe -Algorithm SHA256).Hash
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
[pscustomobject]@{
    Version = $Version
    ProfileRoot = $ProfileRoot
    Executable = $testExe
    ExecutableSha256 = $exeHash
    Zip = $zipPath
    ZipSha256 = $zipHash
}
