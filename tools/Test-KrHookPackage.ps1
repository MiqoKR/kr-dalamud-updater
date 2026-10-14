param(
    [Parameter(Mandatory = $true)][string]$OfficialHookRoot,
    [Parameter(Mandatory = $true)][string]$PayloadRoot,
    [Parameter(Mandatory = $true)][string]$VersionInfoPath,
    [Parameter(Mandatory = $true)][string]$WorkRoot
)
$ErrorActionPreference = 'Stop'
$official = (Resolve-Path -LiteralPath $OfficialHookRoot).Path
$payload = (Resolve-Path -LiteralPath $PayloadRoot).Path
$metadata = Get-Content -LiteralPath $VersionInfoPath -Raw | ConvertFrom-Json
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$work = [IO.Path]::GetFullPath($WorkRoot)
if (-not $work.StartsWith($workspace.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Test output must be inside this workspace.'
}
$root = Join-Path $work ('hook-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$gui = Join-Path $payload 'KrDalamudUpdaterGui.dll'

function New-TestHook([string]$Name) {
    $target = Join-Path $root $Name
    Copy-Item -LiteralPath $official -Destination $target -Recurse
    [IO.File]::WriteAllText((Join-Path $target 'version.json'), ($metadata | ConvertTo-Json -Depth 20))
    return $target
}
function Invoke-Patch([string]$Hook, [bool]$ExpectedSuccess) {
    & dotnet $gui --patch-hook $Hook
    if (($LASTEXITCODE -eq 0) -ne $ExpectedSuccess) { throw "Unexpected patch result for $Hook" }
}
function Assembly-Hashes([string]$Hook) {
    return @('Dalamud.dll', 'FFXIVClientStructs.dll') | ForEach-Object {
        (Get-FileHash -LiteralPath (Join-Path $Hook $_) -Algorithm SHA256).Hash
    }
}

$valid = New-TestHook 'valid'
Invoke-Patch $valid $true
$first = (Assembly-Hashes $valid) -join ','
Invoke-Patch $valid $true
if ($first -ne ((Assembly-Hashes $valid) -join ',')) { throw 'Repeat patch modified assemblies.' }
$hashes = Get-Content -LiteralPath (Join-Path $valid 'hashes.json') -Raw | ConvertFrom-Json
foreach ($name in @('Dalamud.dll', 'FFXIVClientStructs.dll')) {
    if ($hashes.$name -ne (Get-FileHash -LiteralPath (Join-Path $valid $name) -Algorithm MD5).Hash) {
        throw "Updated manifest hash mismatch: $name"
    }
}

$wrongVersion = New-TestHook 'unsupported-game'
$wrongMetadata = $metadata | ConvertTo-Json -Depth 20 | ConvertFrom-Json
$wrongMetadata.supportedGameVer = '2099.01.01.0000.0000'
[IO.File]::WriteAllText((Join-Path $wrongVersion 'version.json'), ($wrongMetadata | ConvertTo-Json -Depth 20))
$before = (Assembly-Hashes $wrongVersion) -join ','
Invoke-Patch $wrongVersion $false
if ($before -ne ((Assembly-Hashes $wrongVersion) -join ',')) { throw 'Rejected version modified assemblies.' }

$tampered = New-TestHook 'tampered-clientstructs'
$stream = [IO.File]::Open((Join-Path $tampered 'FFXIVClientStructs.dll'), [IO.FileMode]::Append)
try { $stream.WriteByte(0) } finally { $stream.Dispose() }
$before = (Assembly-Hashes $tampered) -join ','
Invoke-Patch $tampered $false
if ($before -ne ((Assembly-Hashes $tampered) -join ',')) { throw 'Rejected hash modified assemblies.' }

Write-Output 'PASS: isolated official Hook, repeat apply, hashes, unsupported game and tampered DLL guards'
Write-Output "ValidatedHook=$valid"
$global:LASTEXITCODE = 0
