$ErrorActionPreference = 'Stop'
$releaseRepo = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $releaseRepo 'scripts\Habitat.Release.psm1') -Force
$releaseChecks = Join-Path $releaseRepo ('artifacts\release-checks\' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $releaseChecks 'stage'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($name in 'HABITAT.exe', 'HABITAT.dll', 'Habitat.Core.dll', 'resource.txt') {
    [IO.File]::WriteAllText((Join-Path $stage $name), 'synthetic-' + $name)
}
New-HabitatBuildManifest $stage
Test-HabitatBuildManifest $stage
$first = Install-HabitatRelease -ArtifactsRoot $releaseChecks -StageDirectory $stage
if ((Get-HabitatActiveDirectory $releaseChecks) -ne $first) { throw 'Initial activation failed.' }
[IO.File]::WriteAllText((Join-Path $stage 'resource.txt'), 'damaged')
$rejected = $false
try { Install-HabitatRelease -ArtifactsRoot $releaseChecks -StageDirectory $stage } catch { $rejected = $true }
if (-not $rejected -or (Get-HabitatActiveDirectory $releaseChecks) -ne $first) { throw 'A damaged resource changed the active release.' }
New-HabitatBuildManifest $stage
$rejected = $false
try {
    Install-HabitatRelease -ArtifactsRoot $releaseChecks -StageDirectory $stage -BeforeActivate { throw 'Simulated preparation failure' }
} catch { $rejected = $true }
if (-not $rejected -or (Get-HabitatActiveDirectory $releaseChecks) -ne $first) { throw 'Preparation failure changed the active release.' }
$rejected = $false
try {
    Install-HabitatRelease -ArtifactsRoot $releaseChecks -StageDirectory $stage -AfterActivate { throw 'Simulated startup failure' }
} catch { $rejected = $true }
if (-not $rejected -or (Get-HabitatActiveDirectory $releaseChecks) -ne $first) { throw 'Rollback did not restore the original release.' }
$second = Install-HabitatRelease -ArtifactsRoot $releaseChecks -StageDirectory $stage
if ($second -eq $first -or -not (Test-Path -LiteralPath (Join-Path $first 'HABITAT.exe'))) { throw 'Previous release was overwritten.' }
$rejected = $false
try { Install-HabitatRelease -ArtifactsRoot $releaseChecks -StageDirectory $releaseRepo } catch { $rejected = $true }
if (-not $rejected) { throw 'An out-of-root stage was accepted.' }
$lock = [IO.File]::Open((Join-Path $releaseChecks 'update.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    $rejected = $false
    try { Install-HabitatRelease -ArtifactsRoot $releaseChecks -StageDirectory $stage } catch { $rejected = $true }
    if (-not $rejected) { throw 'Concurrent updates were accepted.' }
} finally { $lock.Dispose() }
Write-Host 'Release checks passed: complete manifests, preparation failure, startup rollback, previous versions, path boundaries and concurrent updates. Synthetic files only.'
