param(
    [string]$InstallRoot = 'C:\proyectos\habitat',
    [string]$PackagePath = (Join-Path $PSScriptRoot 'HABITAT-0.16.4-win-x64.zip'),
    [string]$ExpectedActiveRelease = 'releases\release-e464b7ac0eb54f75a0b9e0e57c50197f',
    [switch]$NoLaunch
)
# Standalone bootstrap: the broken updater in the old installation is never run.
$env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class HabitatZipRecoveryProfile {
    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
}
'@
[uint32]$packageLength = 0
if ([HabitatZipRecoveryProfile]::GetCurrentPackageFullName([ref]$packageLength, [IntPtr]::Zero) -ne 15700 -or
    [Environment]::GetFolderPath('LocalApplicationData') -match '[\\/]sandbox\.[^\\/]+[\\/]') {
    throw 'Run ZIP recovery from your normal Windows session.'
}
$root = (Resolve-Path -LiteralPath $InstallRoot).Path.TrimEnd('\', '/')
$artifacts = Join-Path $root 'artifacts'
$pointer = Join-Path $artifacts 'current-release.txt'
$previousPointer = (Get-Content -LiteralPath $pointer -Raw).Trim()
if ($previousPointer -notmatch '^releases[\\/]release-[a-f0-9]{32}$' -or
    $previousPointer.Replace('\','/') -cne $ExpectedActiveRelease.Replace('\','/') -or
    -not (Test-Path -LiteralPath (Join-Path $root 'HABITAT.vbs')) -or
    (Test-Path -LiteralPath (Join-Path $root 'unins000.exe'))) {
    throw 'The current installation is not the expected development ZIP release. No files were changed.'
}
$previous = [IO.Path]::GetFullPath((Join-Path $artifacts $previousPointer))
if (-not $previous.StartsWith($artifacts + '\', [StringComparison]::OrdinalIgnoreCase) -or
    -not (Test-Path -LiteralPath (Join-Path $previous 'HABITAT.exe'))) { throw 'The old ZIP executable is missing.' }
$packageFile = (Resolve-Path -LiteralPath $PackagePath).Path
$expectedHash = 'FD71A3F1349FB9C41119E27DE9D4EEE79D0FABBC7DE0C98B3CE3E7918A98390B'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$stream = [IO.File]::Open($packageFile, 'Open', 'Read', 'Read')
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $actualHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '')
    if ($actualHash -ne $expectedHash) { throw 'The official 0.16.4 ZIP SHA-256 does not match. No files were changed.' }
    # All writes stay under this installation's artifacts directory. Do not run
    # Install-HABITAT.cmd: its source-file copying is unsuitable for a checkout.
    $work = [IO.Path]::GetFullPath((Join-Path $artifacts ('recovery/' + [guid]::NewGuid().ToString('N'))))
    if (-not $work.StartsWith($artifacts + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid recovery directory.' }
    for ($ancestor = [IO.DirectoryInfo]$root; $ancestor; $ancestor = $ancestor.Parent) {
        if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Recovery requires a plain local installation path.' }
    }
    $unpacked = Join-Path $work 'package'
    New-Item -ItemType Directory -Path $unpacked -Force | Out-Null
    $stream.Position = 0
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        if ($archive.Entries.Count -gt 20000) { throw 'The ZIP has too many entries.' }
        $names = @{}; [long]$total = 0
        foreach ($entry in $archive.Entries) {
            $relative = $entry.FullName.Replace('\','/').TrimEnd('/')
            if (-not $relative -or $relative.StartsWith('/') -or $relative.Contains(':') -or
                @($relative.Split('/') | Where-Object { $_ -in '.', '..' -or $_.TrimEnd(' ', '.') -ne $_ }).Count) { throw 'Unsafe ZIP entry.' }
            $target = [IO.Path]::GetFullPath((Join-Path $unpacked $relative))
            $total += $entry.Length
            if (-not $target.StartsWith($unpacked + '\', [StringComparison]::OrdinalIgnoreCase) -or
                $names.ContainsKey($target) -or $total -gt 2GB -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe or duplicate ZIP entry.' }
            $names[$target] = $true
            if ($entry.FullName.Replace('\','/').EndsWith('/')) { [IO.Directory]::CreateDirectory($target) | Out-Null }
            else {
                [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target)
            }
        }
    } finally { $archive.Dispose() }
} finally { $sha.Dispose(); $stream.Dispose() }
$metadata = Get-Content -LiteralPath (Join-Path $unpacked 'release.json') -Raw | ConvertFrom-Json
if ($metadata.version -ne '0.16.4' -or $metadata.runtime -ne 'win-x64' -or
    $metadata.sourceCommit -ne 'b13d921b12ec8a79d4261d5dca93a1e843b1439c') { throw 'The verified package has unexpected metadata.' }
Import-Module (Join-Path $unpacked 'artifacts/staged/app/updater/Habitat.Update.psm1') -Force
Import-Module (Join-Path $unpacked 'artifacts/staged/app/updater/Habitat.Release.psm1') -Force
Test-HabitatBuildManifest (Join-Path $unpacked 'artifacts/staged/app')
$oldHashes = [ordered]@{}
foreach ($name in 'HABITAT.exe','HABITAT.dll','Habitat.Core.dll','updater/install-release-update.ps1') {
    $oldFile = Join-Path $previous $name
    if (Test-Path -LiteralPath $oldFile -PathType Leaf) { $oldHashes[$name] = (Get-FileHash -LiteralPath $oldFile -Algorithm SHA256).Hash }
}
$oldVersion = if (Test-Path -LiteralPath (Join-Path $previous 'HABITAT.dll')) {
    [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $previous 'HABITAT.dll')).ProductVersion
} else { $null }
@{ phase = 'before-activation'; previousRelease = $previousPointer; previousProductVersion = $oldVersion;
    previousFileHashes = $oldHashes; packageSha256 = $actualHash } | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $work 'before.json') -Encoding UTF8

function RecoveryProcesses([string]$Directory) {
    @(Get-Process -Name HABITAT -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $Directory 'HABITAT.exe') })
}
function RecoveryStatus([int]$ExpectedProcess) {
    $pair = Invoke-RestMethod -Uri 'http://127.0.0.1:8765/pair' -Headers @{ 'X-Habitat-Pair' = '1' } -TimeoutSec 3
    $state = Invoke-RestMethod -Uri 'http://127.0.0.1:8765/status' -Headers @{ 'X-Habitat-Key' = $pair.key } -TimeoutSec 3
    if ($state.processId -ne $ExpectedProcess) { throw 'The bridge belongs to another application instance.' }
    return $state
}
function StopRecoveryRelease([string]$Directory) {
    foreach ($process in @(RecoveryProcesses $Directory)) {
        if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(30000)) {
            Stop-Process -Id $process.Id -ErrorAction Stop
            if (-not $process.WaitForExit(5000)) { throw 'HABITAT did not stop.' }
        }
    }
}
function StartRecoveryRelease([string]$Directory) {
    if ($NoLaunch) { return }
    $process = Start-Process -FilePath (Join-Path $Directory 'HABITAT.exe') -WorkingDirectory $Directory -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(30)
    do {
        if ($process.HasExited) { throw 'The activated application exited before startup completed.' }
        try { if ((RecoveryStatus $process.Id).protocolVersion -ge 1) { return } } catch { }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    throw 'The activated application did not become ready.'
}
$active = Install-HabitatZipUpdate -PackageRoot $unpacked -InstallRoot $root -Version '0.16.4' -BeforeActivate {
    param($old)
    if ((Get-Content -LiteralPath $pointer -Raw).Trim() -ne $previousPointer) { throw 'The active release changed during recovery.' }
    $running = @(RecoveryProcesses $old)
    if ($NoLaunch -and $running.Count) { throw 'Close the isolated fixture before using NoLaunch.' }
    foreach ($process in $running) {
        if ((RecoveryStatus $process.Id).hasMessageInFlight) { throw 'HABITAT is busy. Finish the current send and retry recovery.' }
    }
    StopRecoveryRelease $old
    $companion = Join-Path $old 'HABITAT.Hotkey.exe'
    if (Get-Process -Name HABITAT.Hotkey -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $companion }) {
        $stop = Start-Process -FilePath $companion -ArgumentList '--stop' -WindowStyle Hidden -PassThru
        if (-not $stop.WaitForExit(15000) -or $stop.ExitCode -ne 0) { throw 'The shortcut component could not stop.' }
    }
} -StartRelease { param($directory) StartRecoveryRelease $directory } -StopRelease { param($directory) StopRecoveryRelease $directory }
@{ version = '0.16.4'; packageSha256 = $actualHash; previousRelease = $previousPointer;
    activeRelease = $active.Substring($artifacts.Length + 1); startupVerified = -not $NoLaunch;
    previousProductVersion = $oldVersion; previousFileHashes = $oldHashes } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'result.json') -Encoding UTF8
Write-Output 'HABITAT 0.16.4 activated from the official ZIP. Source files and saved user data were not replaced.'
if ($NoLaunch) { Write-Output 'Startup was skipped for the isolated installation check.' }
