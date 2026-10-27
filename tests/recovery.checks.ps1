param([Parameter(Mandatory)][string]$PackagePath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path ([IO.Path]::GetTempPath()) ('HabitatUpdateChecks/artifacts/recovery-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$oldRelative = 'releases\release-e464b7ac0eb54f75a0b9e0e57c50197f'
$old = Join-Path $root ('artifacts/' + $oldRelative)
New-Item -ItemType Directory -Path $old -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $old 'HABITAT.exe'), 'old synthetic executable; never started')
[IO.File]::WriteAllText((Join-Path $root 'HABITAT.vbs'), 'old synthetic launcher; never started')
[IO.File]::WriteAllText((Join-Path $root 'artifacts/current-release.txt'), $oldRelative)
$protected = @('src/code.cs','scripts/user-script.ps1','docs/user-guide.md','.git/user-marker','profile/tasks.json','profile/preferences.json','profile/credentials-marker.bin')
foreach ($name in $protected) {
    $file = Join-Path $root $name
    New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null
    [IO.File]::WriteAllText($file, 'preserve exactly: ' + $name)
}
$invalid = Join-Path $root 'invalid.zip'
[IO.File]::WriteAllText($invalid, 'invalid official ZIP hash')
$rejected = $false
try { & (Join-Path $repo 'scripts/recover-development-zip.ps1') -InstallRoot $root -PackagePath $invalid -NoLaunch }
catch { $rejected = $_.Exception.Message -like '*SHA-256*' }
if (-not $rejected -or (Get-Content -LiteralPath (Join-Path $root 'artifacts/current-release.txt') -Raw) -ne $oldRelative) {
    throw 'An invalid package changed the synthetic active release.'
}
# A new PowerShell process avoids leaking the recovery script's variables into
# this test scope. No real HABITAT executable, profile or bridge is started.
$run = Start-Process -FilePath "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File', ('"' + (Join-Path $repo 'scripts/recover-development-zip.ps1') + '"'),
    '-InstallRoot', ('"' + $root + '"'), '-PackagePath', ('"' + (Resolve-Path -LiteralPath $PackagePath).Path + '"'), '-NoLaunch') -WindowStyle Hidden -PassThru
while (-not $run.WaitForExit(1000)) { }
if ($run.ExitCode -ne 0) { throw 'Official ZIP recovery failed on the isolated development installation.' }
foreach ($name in $protected) {
    if ([IO.File]::ReadAllText((Join-Path $root $name)) -ne 'preserve exactly: ' + $name) { throw "Recovery changed a protected source/profile fixture: $name" }
}
if (-not (Test-Path -LiteralPath (Join-Path $old 'HABITAT.exe'))) { throw 'Recovery removed the previous release.' }
$active = (Get-Content -LiteralPath (Join-Path $root 'artifacts/current-release.txt') -Raw).Trim()
if ($active -eq $oldRelative) { throw 'Recovery did not activate a new release.' }
$metadata = Get-Content -LiteralPath (Join-Path $root ('artifacts/' + $active + '/release.json')) -Raw | ConvertFrom-Json
if ($metadata.version -ne '0.16.4' -or $metadata.sourceCommit -ne 'b13d921b12ec8a79d4261d5dca93a1e843b1439c') {
    throw 'Recovery did not install the exact official release.'
}
$beforeFile = @(Get-ChildItem -LiteralPath (Join-Path $root 'artifacts/recovery') -Filter before.json -Recurse)
$before = Get-Content -LiteralPath $beforeFile[0].FullName -Raw | ConvertFrom-Json
if ($before.previousFileHashes.'HABITAT.exe' -ne (Get-FileHash -LiteralPath (Join-Path $old 'HABITAT.exe')).Hash) {
    throw 'Recovery did not preserve the original binary identity before activation.'
}
Write-Output 'PASS: official ZIP recovery verifies SHA-256, activates a fresh release, retains the old release and preserves source, Git markers and synthetic user data. No real application was started.'
