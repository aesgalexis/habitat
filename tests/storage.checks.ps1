param([string]$TestDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repo 'scripts/Habitat.Release.psm1') -Force
Import-Module (Join-Path $repo 'scripts/Habitat.Storage.psm1') -Force
$testBase = if ($TestDirectory) { [IO.Path]::GetFullPath($TestDirectory) } else { Join-Path $repo 'artifacts' }
$testRun = Join-Path $testBase ('storage-checks/run-' + [guid]::NewGuid().ToString('N'))
$root = Join-Path $testRun 'installation/artifacts'
[IO.Directory]::CreateDirectory($root) | Out-Null
[IO.File]::WriteAllText((Join-Path (Split-Path -Parent $root) 'HABITAT.vbs'), 'synthetic installation marker')
$clock = [DateTime]::UtcNow.AddDays(30)
$token = [Threading.CancellationToken]::None
$inheritedDryRun = $env:HABITAT_STORAGE_DRY_RUN
# Destructive checks target only the unique synthetic installation below.
$env:HABITAT_STORAGE_DRY_RUN = $null
try {
$script:count = 0
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:count++
}
function Stage([string]$Directory) {
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    foreach ($name in 'HABITAT.exe', 'HABITAT.dll', 'Habitat.Core.dll', 'Assets/resource.txt') {
        $file = Join-Path $Directory $name
        [IO.Directory]::CreateDirectory((Split-Path -Parent $file)) | Out-Null
        [IO.File]::WriteAllText($file, 'synthetic generated ' + $name)
    }
    New-HabitatBuildManifest $Directory
    return $Directory
}
function Scan([bool]$Clean = $false) { [Habitat.Storage.GeneratedStorage]::RunAt($root, $Clean, $null, $clock, $token) }

$exeRoot = Join-Path $testRun 'exe-installation'
$profileRoot = Join-Path $testRun 'isolated-profile'
[IO.Directory]::CreateDirectory($exeRoot) | Out-Null
[IO.Directory]::CreateDirectory($profileRoot) | Out-Null
[IO.File]::WriteAllText((Join-Path $exeRoot 'HABITAT.exe'), 'synthetic product')
$binding = Join-Path $profileRoot 'generated-storage.json'
Check ($null -eq [Habitat.Storage.GeneratedStorage]::ReadRegisteredRoot($exeRoot, $binding)) 'An EXE installation must not discover unregistered source trees.'
[Habitat.Storage.GeneratedStorage]::RegisterRoot($exeRoot, $root, $binding)
Check ([Habitat.Storage.GeneratedStorage]::ReadRegisteredRoot($exeRoot, $binding) -eq $root) 'Explicit per-machine storage registration must resolve the correct tree.'
Check ($null -eq [Habitat.Storage.GeneratedStorage]::ReadRegisteredRoot((Join-Path $testRun 'other-installation'), $binding)) 'A storage registration must remain bound to its exact installation.'
$validBinding = [IO.File]::ReadAllText($binding)
[IO.File]::WriteAllText($binding, '{"version":1,"installationRoot":"invalid","artifactsRoot":"../outside"}')
Check ($null -eq [Habitat.Storage.GeneratedStorage]::ReadRegisteredRoot($exeRoot, $binding)) 'An invalid storage registration must fail closed.'
[IO.File]::WriteAllText($binding, $validBinding)

$stages = @(); $releases = @()
foreach ($index in 1..4) {
    $stage = Stage (Join-Path $root ('staged/build-' + [guid]::NewGuid().ToString('N')))
    $stages += $stage
    $releases += Install-HabitatRelease -ArtifactsRoot $root -StageDirectory $stage
}
$pending = Stage (Join-Path $root ('staged/build-' + [guid]::NewGuid().ToString('N')))
$inProgress = Stage (Join-Path $root ('staged/build-' + [guid]::NewGuid().ToString('N')))
$operation = [Habitat.Storage.GeneratedStorage]::BeginOperation($root, @($inProgress))
try {
    $analysis = Scan
    Check (-not $analysis.Unavailable -and -not $analysis.Busy -and $analysis.RecoverableBytes -gt 0) 'Dry run must identify old verified outputs.'
    Check (@($releases | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 4) 'Analysis deleted a release.'
    $cleaned = Scan $true
    Check (-not $cleaned.Unavailable -and $cleaned.Errors.Count -eq 0 -and $cleaned.FreedBytes -eq $analysis.RecoverableBytes) ('Cleanup accounting must match deleted bytes: ' + ($cleaned | ConvertTo-Json -Depth 4))
    Check ((Test-Path -LiteralPath $releases[3]) -and (Test-Path -LiteralPath $releases[2])) 'Active and previous releases must survive.'
    Check (-not (Test-Path -LiteralPath $releases[0]) -and -not (Test-Path -LiteralPath $releases[1])) 'Obsolete verified releases were not removed.'
    Check ((Test-Path -LiteralPath $pending) -and (Test-Path -LiteralPath $inProgress)) 'Pending and leased stages must survive.'
    Check (@($stages | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0) 'Consumed stages must be retired after the safety margin.'
    Check ((Scan $true).FreedBytes -eq 0) 'Cleanup must be repeatable.'
} finally { $operation.Dispose() }

# When the recorded previous release is missing, a complete alternative survives.
[IO.File]::WriteAllText((Join-Path $root 'previous-release.txt'), 'releases/release-00000000000000000000000000000000')
$fallback = Scan $true
Check (Test-Path -LiteralPath $releases[2]) 'A missing previous pointer must retain a verified fallback.'
[IO.File]::WriteAllText((Join-Path $root 'previous-release.txt'), [IO.File]::ReadAllText((Join-Path $root 'current-release.txt')))
$null = Scan $true
Check (Test-Path -LiteralPath $releases[2]) 'A previous pointer equal to active must still retain a separate usable recovery release.'

$legacyRoot = Join-Path $testRun 'legacy/artifacts'
[IO.Directory]::CreateDirectory($legacyRoot) | Out-Null
$legacy = Stage (Join-Path $legacyRoot 'win-x64')
$legacyActive = Stage (Join-Path $legacyRoot ('releases/release-' + [guid]::NewGuid().ToString('N')))
[IO.File]::WriteAllText((Join-Path $legacyRoot 'current-release.txt'), 'releases/' + (Split-Path -Leaf $legacyActive))
$null = [Habitat.Storage.GeneratedStorage]::RunAt($legacyRoot, $true, $null, $clock, $token)
Check (Test-Path -LiteralPath $legacy) 'A complete legacy win-x64 build must remain available as recovery.'
$legacyPrevious = Stage (Join-Path $legacyRoot ('releases/release-' + [guid]::NewGuid().ToString('N')))
[IO.File]::WriteAllText((Join-Path $legacyRoot 'previous-release.txt'), 'releases/' + (Split-Path -Leaf $legacyPrevious))
$null = [Habitat.Storage.GeneratedStorage]::RunAt($legacyRoot, $true, $null, $clock, $token)
Check (-not (Test-Path -LiteralPath $legacy) -and (Test-Path -LiteralPath $legacyActive) -and (Test-Path -LiteralPath $legacyPrevious)) 'A legacy build may be retired only when active and verified recovery versions are protected.'

# Invalid paths fail closed before any candidate can be removed.
$pointer = [IO.File]::ReadAllText((Join-Path $root 'current-release.txt'))
foreach ($invalid in '../outside', 'releases/release-00000000000000000000000000000000/../../outside', 'C:\outside', 'win-x64:stream') {
    [IO.File]::WriteAllText((Join-Path $root 'current-release.txt'), $invalid)
    Check ((Scan $true).Unavailable -and (Test-Path -LiteralPath $releases[3])) 'An unsafe pointer must disable cleanup.'
}
[IO.File]::WriteAllText((Join-Path $root 'current-release.txt'), $pointer)
$lock = [Habitat.Storage.GeneratedStorage]::AcquireLock($root)
try {
    Check ((Scan $true).Busy) 'The updater and cleaner must share an exclusive lock.'
    $rejected = $false
    try { [Habitat.Storage.GeneratedStorage]::BeginOperation($root, @($pending)) } catch { $rejected = $true }
    Check $rejected 'A producer must not register during cleanup or activation.'
} finally { $lock.Dispose() }

# Unknown folders and altered output are retained, irrespective of their age/name.
$unknown = Join-Path $root 'recovery-backup'
[IO.Directory]::CreateDirectory($unknown) | Out-Null
[IO.File]::WriteAllText((Join-Path $unknown 'tasks.json'), 'personal sentinel')
$altered = Stage (Join-Path $root ('staged/altered-' + [guid]::NewGuid().ToString('N')))
[IO.File]::WriteAllText((Join-Path $altered 'notes.txt'), 'local data must survive')
$locked = Stage (Join-Path $root ('staged/locked-' + [guid]::NewGuid().ToString('N')))
$held = [IO.File]::Open((Join-Path $locked 'Habitat.Core.dll'), 'Open', 'ReadWrite', 'None')
$disposable = Stage (Join-Path $root ('staged/disposable-' + [guid]::NewGuid().ToString('N')))
[Habitat.Storage.GeneratedStorage]::Complete($root, $disposable, 'consumed')
try {
    $partial = Scan $true
    Check ((Test-Path -LiteralPath $locked) -and (Test-Path -LiteralPath $altered) -and (Test-Path -LiteralPath $unknown)) 'Locked, altered and unclassified folders must survive.'
    Check (-not (Test-Path -LiteralPath $disposable) -and $partial.FreedBytes -gt 0 -and $partial.Errors.Count -gt 0) 'A locked output must not prevent independent cleanup.'
} finally { $held.Dispose() }

# Proof for historical installer work is the complete payload manifest.
$payload = Stage (Join-Path $root ('packaging/' + [guid]::NewGuid().ToString('N') + '/payload'))
Check ((Scan $true).FreedBytes -gt 0 -and -not (Test-Path -LiteralPath $payload)) 'Historical manifested installer payload was not classified.'

# Newly registered tests/packages need a complete, unchanged output inventory.
$testOutput = Join-Path $root ('verification/run-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testOutput) | Out-Null
[IO.File]::WriteAllText((Join-Path $testOutput 'synthetic-tasks.json'), 'isolated test data')
[Habitat.Storage.GeneratedStorage]::Complete($root, $testOutput, 'test')
$fresh = [Habitat.Storage.GeneratedStorage]::Run($root, $true, $null, $token)
Check (Test-Path -LiteralPath $testOutput) 'Fresh completed outputs need seven days of grace.'
$testOperation = [Habitat.Storage.GeneratedStorage]::BeginOperation($root, @($testOutput))
try { Check ((Scan $true).Eligible -notcontains $testOutput.Substring($root.Length + 1).Replace('\', '/')) 'A running test output must remain protected.' }
finally { $testOperation.Dispose() }
Check ((Scan $true).FreedBytes -gt 0 -and -not (Test-Path -LiteralPath $testOutput)) 'Completed isolated test output was not retired.'
$failedBuild = Join-Path $root ('staged/build-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($failedBuild) | Out-Null
[IO.File]::WriteAllText((Join-Path $failedBuild 'partial-output.dll'), 'incomplete generated build')
[Habitat.Storage.GeneratedStorage]::Complete($root, $failedBuild, 'build')
Check ((Scan $true).FreedBytes -gt 0 -and -not (Test-Path -LiteralPath $failedBuild)) 'A returned, failed build needs a producer inventory before its partial output may be retired.'
$longOutput = Join-Path $root ('verification/run-' + [guid]::NewGuid().ToString('N'))
$longDirectory = Join-Path $longOutput (('x' * 70) + '/' + ('y' * 70) + '/' + ('z' * 70))
[IO.Directory]::CreateDirectory(('\\?\' + $longDirectory)) | Out-Null
[IO.File]::WriteAllText(('\\?\' + (Join-Path $longDirectory 'generated.bin')), 'long-path generated output')
[Habitat.Storage.GeneratedStorage]::Complete($root, $longOutput, 'test')
Check ((Scan $true).FreedBytes -gt 0 -and -not [IO.Directory]::Exists($longOutput)) 'Long generated paths need the same boundary checks, inventory validation and safe handle deletion.'

# A journal permits retry after interrupted deletion, but rejects new content.
$retry = Join-Path $root ('verification/run-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($retry) | Out-Null
[IO.File]::WriteAllText((Join-Path $retry 'one.bin'), 'one')
[IO.File]::WriteAllText((Join-Path $retry 'two.bin'), 'two')
[Habitat.Storage.GeneratedStorage]::Complete($root, $retry, 'test')
$receiptFile = Get-ChildItem -LiteralPath (Join-Path $root '.storage-receipts') -Filter '*.json' | Where-Object { ([IO.File]::ReadAllText($_.FullName) | ConvertFrom-Json).path -eq $retry.Substring($root.Length + 1).Replace('\', '/') }
$receipt = [IO.File]::ReadAllText($receiptFile.FullName).Replace('"test"', '"cleanup"')
[IO.File]::WriteAllText($receiptFile.FullName, $receipt)
[IO.File]::Delete((Join-Path $retry 'one.bin'))
Check ((Scan $true).FreedBytes -gt 0 -and -not (Test-Path -LiteralPath $retry)) 'Interrupted deletion must safely retry unchanged remaining files.'

# Junctions inside and above the target must never be traversed.
$outside = Join-Path $testRun 'outside'
[IO.Directory]::CreateDirectory($outside) | Out-Null
[IO.File]::WriteAllText((Join-Path $outside 'sentinel.txt'), 'outside data')
$linkedStage = Join-Path $root 'staged/linked'
New-Item -ItemType Junction -Path $linkedStage -Target $outside | Out-Null
$withLink = Stage (Join-Path $root ('staged/with-link-' + [guid]::NewGuid().ToString('N')))
New-Item -ItemType Junction -Path (Join-Path $withLink 'nested') -Target $outside | Out-Null
$null = Scan $true
Check ([IO.File]::ReadAllText((Join-Path $outside 'sentinel.txt')) -eq 'outside data' -and (Test-Path -LiteralPath $withLink)) 'Cleanup crossed a nested junction.'
$linkedParent = Join-Path $testRun 'linked-parent'
New-Item -ItemType Junction -Path $linkedParent -Target (Split-Path -Parent $root) | Out-Null
Check ([Habitat.Storage.GeneratedStorage]::RunAt((Join-Path $linkedParent 'artifacts'), $true, $null, $clock, $token).Unavailable) 'Cleanup crossed a junction above its root.'
$readonly = Stage (Join-Path $root ('staged/readonly-' + [guid]::NewGuid().ToString('N')))
[IO.File]::SetAttributes((Join-Path $readonly 'HABITAT.exe'), [IO.FileAttributes]::ReadOnly)
$null = Scan $true
Check (Test-Path -LiteralPath $readonly) 'Read-only output must remain usable after a cleanup error.'

$emptyAdded = Join-Path $root ('verification/run-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($emptyAdded) | Out-Null
[IO.File]::WriteAllText((Join-Path $emptyAdded 'generated.bin'), 'generated')
[Habitat.Storage.GeneratedStorage]::Complete($root, $emptyAdded, 'test')
[IO.Directory]::CreateDirectory((Join-Path $emptyAdded 'unclassified-empty-folder')) | Out-Null
$null = Scan $true
Check (Test-Path -LiteralPath (Join-Path $emptyAdded 'generated.bin')) 'An added empty directory must invalidate the recorded output inventory.'

$hardLinked = Stage (Join-Path $root ('staged/hardlinked-' + [guid]::NewGuid().ToString('N')))
New-Item -ItemType HardLink -Path (Join-Path $hardLinked 'external-link.txt') -Target (Join-Path $outside 'sentinel.txt') | Out-Null
New-HabitatBuildManifest $hardLinked
$null = Scan $true
Check ((Test-Path -LiteralPath $hardLinked) -and [IO.File]::ReadAllText((Join-Path $outside 'sentinel.txt')) -eq 'outside data') 'Hard-linked output must not be classified as disposable.'

# A verified ancestor cannot be renamed and replaced with a junction while pinned.
$pinType = [Habitat.Storage.GeneratedStorage].GetNestedType('Pins', [Reflection.BindingFlags]::NonPublic)
$pins = $pinType.GetConstructor([type[]]@([bool], [string])).Invoke([object[]]@($true, [string]$root))
$pinnedDirectory = Join-Path $root 'pin-race'
[IO.Directory]::CreateDirectory($pinnedDirectory) | Out-Null
try {
    $null = $pinType.GetMethod('Add').Invoke($pins, [object[]]@([string]$pinnedDirectory))
    $renamed = $false
    try { [IO.Directory]::Move($pinnedDirectory, (Join-Path $root 'pin-race-swapped')); $renamed = $true } catch { }
    Check (-not $renamed) 'Pinned directory identities must prevent rename/junction substitution races.'
} finally { $pins.Dispose() }

$runner = Join-Path $testRun 'SyntheticRunner.exe'
$runnerSource = Join-Path $testRun 'SyntheticRunner.cs'
[IO.File]::WriteAllText($runnerSource, 'public static class SyntheticRunner { public static void Main() { System.Threading.Thread.Sleep(120000); } }')
$runnerCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $runnerCompiler /nologo /target:winexe ('/out:' + $runner) $runnerSource
Check ($LASTEXITCODE -eq 0) 'The isolated process fixture must compile.'
$runningRelease = Stage (Join-Path $root ('releases/release-' + [guid]::NewGuid().ToString('N')))
Copy-Item -LiteralPath $runner -Destination (Join-Path $runningRelease 'HABITAT.exe') -Force
New-HabitatBuildManifest $runningRelease
$running = Start-Process -FilePath (Join-Path $runningRelease 'HABITAT.exe') -WindowStyle Hidden -PassThru
try {
    $runningReport = Scan $true
    Check (Test-Path -LiteralPath (Join-Path $runningRelease 'Habitat.Core.dll')) 'A running release outside the current/previous pointers must retain all of its files.'
    Check ($runningReport.Preserved -contains ($runningRelease.Substring($root.Length + 1).Replace('\', '/') + ': active, recovery or operation')) 'The cleaner must explicitly protect the running executable directory.'
} finally { if (-not $running.HasExited) { $running.Kill(); $running.WaitForExit() }; $running.Dispose() }

$cancel = [Threading.CancellationTokenSource]::new()
$cancel.Cancel()
$cancelled = $false
try { [Habitat.Storage.GeneratedStorage]::RunAt($root, $true, $null, $clock, $cancel.Token) } catch { $cancelled = $true }
finally { $cancel.Dispose() }
Check ($cancelled -and (Test-Path -LiteralPath $releases[3])) 'Cancellation must stop maintenance without touching the active release.'
Check ([Habitat.Storage.GeneratedStorage]::RunAt($root, $false, $null, [DateTime]::UtcNow.AddDays(6), $token).RecoverableBytes -eq 0) 'The grace period must apply independently to all recent outputs.'
Check ([Habitat.Storage.GeneratedStorage]::RunAt((Split-Path -Parent $root), $true, $null, $clock, $token).Unavailable) 'The generated root itself must never become a cleanup candidate.'
foreach ($tag in @('9000001A','9000701A','9000F01A')) {
    Check ([Habitat.Storage.GeneratedStorage]::IsCloudMetadataTag([uint32]::Parse($tag, [Globalization.NumberStyles]::HexNumber))) 'A documented Cloud Files tag was not recognized.'
}
foreach ($tag in @('A0000003','A000000C','9000001B','80000021')) {
    Check (-not [Habitat.Storage.GeneratedStorage]::IsCloudMetadataTag([uint32]::Parse($tag, [Globalization.NumberStyles]::HexNumber))) 'A junction, link or unknown tag was mistaken for Cloud Files metadata.'
}

# Rollback restores both pointers, preserving the previous recovery chain.
[IO.File]::WriteAllText((Join-Path $root 'previous-release.txt'), 'releases/' + (Split-Path -Leaf $releases[2]))
$before = [IO.File]::ReadAllText((Join-Path $root 'previous-release.txt'))
$rollbackStage = Stage (Join-Path $root ('staged/build-' + [guid]::NewGuid().ToString('N')))
try { Install-HabitatRelease -ArtifactsRoot $root -StageDirectory $rollbackStage -AfterActivate { throw 'Synthetic startup failure' } } catch { }
Check ([IO.File]::ReadAllText((Join-Path $root 'previous-release.txt')) -eq $before -and [IO.File]::ReadAllText((Join-Path $root 'current-release.txt')) -eq $pointer) 'Rollback must restore both active and previous pointers.'
Write-Host "Storage checks passed: $script:count assertions. Synthetic installations only: $testRun"
} finally { $env:HABITAT_STORAGE_DRY_RUN = $inheritedDryRun }
