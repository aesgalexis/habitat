$ErrorActionPreference = 'Stop'
$updateRepo = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $updateRepo 'scripts/Habitat.Release.psm1') -Force
Import-Module (Join-Path $updateRepo 'scripts/Habitat.Update.psm1') -Force
$testRoot = Join-Path $updateRepo ('artifacts/update-checks/' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $testRoot 'synthetic package'
$installed = Join-Path $testRoot 'installation with spaces'
$payload = Join-Path $package 'artifacts/staged/app'
New-Item -ItemType Directory -Path $payload, $installed, (Join-Path $package 'extension'), (Join-Path $package 'vscode-extension') -Force | Out-Null
foreach ($name in 'HABITAT.exe', 'HABITAT.dll', 'Habitat.Core.dll', 'HABITAT.Hotkey.exe', 'HABITAT.Hotkey.dll', 'Habitat.Activation.dll') {
    [IO.File]::WriteAllText((Join-Path $payload $name), 'synthetic ' + $name)
}
[IO.File]::WriteAllText((Join-Path $package 'release.json'), '{"version":"0.11.0","runtime":"win-x64"}')
[IO.File]::WriteAllText((Join-Path $package 'extension/manifest.json'), '{"version":"1.0.0"}')
[IO.File]::WriteAllText((Join-Path $package 'vscode-extension/test.vsix'), 'synthetic VSIX')
[IO.File]::WriteAllText((Join-Path $installed 'README.md'), 'source must be preserved')
New-HabitatBuildManifest $payload
$first = Install-HabitatZipUpdate -PackageRoot $package -InstallRoot $installed -Version '0.11.0' -BeforeActivate {} -StartRelease {} -StopRelease {}
if ((Get-HabitatActiveDirectory (Join-Path $installed 'artifacts')) -ne $first -or
    -not (Test-Path -LiteralPath (Join-Path $first 'browser-extension/manifest.json')) -or
    -not (Test-Path -LiteralPath (Join-Path $first 'vscode-extension/test.vsix')) -or
    [IO.File]::ReadAllText((Join-Path $installed 'README.md')) -ne 'source must be preserved') { throw 'ZIP update did not preserve sources and matching integrations.' }
$script:updateStarts = 0
$rejected = $false
try {
    $null = Install-HabitatZipUpdate -PackageRoot $package -InstallRoot $installed -Version '0.11.0' -BeforeActivate {} `
        -StartRelease { param($directory) $script:updateStarts++; if ($directory -ne $first) { throw 'Synthetic failed startup' } } -StopRelease {}
} catch { $rejected = $true }
if (-not $rejected -or $script:updateStarts -ne 2 -or
    (Get-HabitatActiveDirectory (Join-Path $installed 'artifacts')) -ne $first) { throw 'Failed startup did not restore and restart the previous release.' }
$rejected = $false
try { $null = Install-HabitatZipUpdate -PackageRoot $package -InstallRoot $installed -Version '0.12.0' -BeforeActivate {} -StartRelease {} -StopRelease {} } catch { $rejected = $true }
if (-not $rejected) { throw 'Incorrect metadata was accepted.' }
$rejected = $false
try { Assert-HabitatUpdatePath $testRoot (Join-Path $testRoot '../escape') } catch { $rejected = $true }
if (-not $rejected) { throw 'An update escaped its installation boundary.' }
$junctionTarget = Join-Path $testRoot 'junction target'
$junctionPath = Join-Path $testRoot 'linked installation'
New-Item -ItemType Directory -Path $junctionTarget -Force | Out-Null
New-Item -ItemType Junction -Path $junctionPath -Target $junctionTarget | Out-Null
$rejected = $false
try { Assert-HabitatUpdatePath $testRoot (Join-Path $junctionPath 'payload') } catch { $rejected = $true }
if (-not $rejected) { throw 'An update followed a junction inside its installation boundary.' }
$rejected = $false
try { Assert-HabitatUpdatePath (Join-Path $junctionPath 'root') (Join-Path $junctionPath 'root/payload') } catch { $rejected = $true }
if (-not $rejected) { throw 'An update followed a junction above its installation boundary.' }
foreach ($hexTag in '9000001A', '9000601A', '9000701A', '9000F01A') {
    $tag = [uint32]::Parse($hexTag, [Globalization.NumberStyles]::HexNumber)
    if (-not [HabitatPackagingPath]::IsCloudTag($tag)) { throw 'A documented Cloud Files tag was mistaken for a filesystem link.' }
}
foreach ($hexTag in 'A0000003', 'A000000C', '80000021', '9000001B', 'B000001A') {
    $tag = [uint32]::Parse($hexTag, [Globalization.NumberStyles]::HexNumber)
    if ([HabitatPackagingPath]::IsCloudTag($tag)) { throw 'A junction, symlink, unknown or name-surrogate tag was accepted as Cloud Files metadata.' }
}
$arguments = Get-HabitatInstallerArguments $installed (Join-Path $testRoot 'installer log.txt')
if (-not $arguments.Contains('/DIR="' + $installed + '"') -or -not $arguments.Contains('/NORESTART') -or -not $arguments.Contains('/VERYSILENT')) { throw 'The EXE updater lost its directory or unattended arguments.' }
foreach ($file in 'scripts/install-release-update.ps1', 'scripts/Habitat.Update.psm1') {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $updateRepo $file), [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw "Updater syntax is invalid: $file" }
}
Write-Host 'Update installation checks passed: isolated ZIP activation, matching integrations, preserved source, failed-start rollback, metadata, path boundaries and silent EXE arguments. No real app or user data used.'
