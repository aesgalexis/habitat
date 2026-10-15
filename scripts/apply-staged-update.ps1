param(
    [Parameter(Mandatory = $true)]
    [string]$StageDirectory
)

$ErrorActionPreference = "Stop"
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HabitatUpdateProfile {
    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
}
'@
[uint32]$updatePackageLength = 0
if ([HabitatUpdateProfile]::GetCurrentPackageFullName([ref]$updatePackageLength, [IntPtr]::Zero) -ne 15700) {
    throw "Run HABITAT updates from Windows outside a packaged application. Inherited package identity can redirect user data."
}
if ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData) -match '[\\/]sandbox\.[^\\/]+[\\/]') {
    throw "Run HABITAT updates from the normal Windows session, outside the temporary sandbox. User data must stay in the persistent profile."
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$stagedRoot = (Resolve-Path -LiteralPath $StageDirectory).Path
$updateLog = Join-Path $artifactsRoot 'update.log'
Import-Module (Join-Path $PSScriptRoot 'Habitat.Release.psm1') -Force

function Write-UpdateLog([string]$Message) {
    Add-Content -LiteralPath $updateLog -Value ("{0:O} {1}" -f (Get-Date), $Message)
}
function Get-ProjectProcess([string]$Directory) {
    @(Get-Process -Name HABITAT -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -eq (Join-Path $Directory 'HABITAT.exe')
    })
}
function Stop-ProjectProcess([string]$Directory) {
    foreach ($process in @(Get-ProjectProcess $Directory)) {
        if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(30000)) {
            Stop-Process -Id $process.Id -ErrorAction Stop
            if (-not $process.WaitForExit(5000)) { throw 'HABITAT did not exit.' }
        }
    }
}
function Read-BridgeStatus([int]$ExpectedProcess) {
    $pair = Invoke-RestMethod -Uri 'http://127.0.0.1:8765/pair' -Headers @{ 'X-Habitat-Pair' = '1' } -TimeoutSec 3
    $status = Invoke-RestMethod -Uri 'http://127.0.0.1:8765/status' -Headers @{ 'X-Habitat-Key' = $pair.key } -TimeoutSec 3
    if ($status.processId) {
        if ($status.processId -ne $ExpectedProcess) { throw 'Bridge belongs to a different process.' }
    } else {
        # Compatibility with the previously installed bridge, which did not
        # include processId. Verify the listener owner instead of UI text.
        $owners = @(Get-NetTCPConnection -LocalPort 8765 -State Listen -ErrorAction Stop | Select-Object -ExpandProperty OwningProcess)
        if ($owners -notcontains $ExpectedProcess) { throw 'Bridge belongs to a different process.' }
    }
    return $status
}
function Start-ProjectRelease([string]$Directory) {
    Start-Process -FilePath "$env:WINDIR\System32\wscript.exe" `
        -ArgumentList ('"' + (Join-Path $projectRoot 'HABITAT.vbs') + '"') -WindowStyle Hidden
    $deadline = (Get-Date).AddSeconds(25)
    do {
        $running = @(Get-ProjectProcess $Directory)
        if ($running.Count -eq 1) {
            try {
                $status = Read-BridgeStatus $running[0].Id
                if ($status.protocolVersion -ge 1) { return }
            } catch { }
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    throw 'The new release did not become ready.'
}
try {
    $installed = Install-HabitatRelease -ArtifactsRoot $artifactsRoot -StageDirectory $stagedRoot -BeforeActivate {
        param($previous)
        Write-UpdateLog 'Waiting for the current ChatGPT response before activating the verified build.'
        $deadline = (Get-Date).AddMinutes(15)
        do {
            $running = @(Get-ProjectProcess $previous)
            if ($running.Count -eq 0) { break }
            if ($running.Count -ne 1) { throw 'More than one HABITAT process uses the active release.' }
            try { $busy = (Read-BridgeStatus $running[0].Id).hasMessageInFlight }
            catch { $busy = $true }
            if (-not $busy) { break }
            if ((Get-Date) -ge $deadline) { throw 'Update timed out; the running app was left open.' }
            Start-Sleep -Seconds 2
        } while ($true)
        Stop-ProjectProcess $previous
    } -AfterActivate {
        param($release)
        Start-ProjectRelease $release
    } -BeforeRollback {
        param($failed)
        Stop-ProjectProcess $failed
    } -AfterRollback {
        param($previous)
        if (Test-Path -LiteralPath (Join-Path $previous 'HABITAT.exe')) {
            Start-Process -FilePath "$env:WINDIR\System32\wscript.exe" `
                -ArgumentList ('"' + (Join-Path $projectRoot 'HABITAT.vbs') + '"') -WindowStyle Hidden
        }
    }
    Write-UpdateLog "Verified release activated: $installed. Previous release retained."
} catch {
    Write-UpdateLog ('Update failed: ' + $_.Exception.Message)
    throw
}
