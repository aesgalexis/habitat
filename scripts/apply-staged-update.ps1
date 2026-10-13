param(
    [Parameter(Mandatory = $true)]
    [string]$StageDirectory
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$stagedRoot = (Resolve-Path -LiteralPath $StageDirectory).Path
$targetRoot = Join-Path $artifactsRoot "win-x64"
$executable = Join-Path $targetRoot "HABITAT.exe"
$updateLog = Join-Path $artifactsRoot "update.log"

function Write-UpdateLog([string]$Message) {
    Add-Content -LiteralPath $updateLog -Value ("{0:O} {1}" -f (Get-Date), $Message)
}

function Get-AssemblyHash([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($algorithm.ComputeHash($stream)) }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}

try {
    if (-not $stagedRoot.StartsWith($artifactsRoot + "\", [StringComparison]::OrdinalIgnoreCase) -or
        $stagedRoot.Equals($targetRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath (Join-Path $stagedRoot "HABITAT.dll"))) {
        throw "The staged build must be a separate directory inside HABITAT artifacts."
    }

    Write-UpdateLog "Waiting for the current ChatGPT response before applying the staged build."
    $deadline = (Get-Date).AddMinutes(15)
    do {
        $running = @(Get-Process -Name HABITAT -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $executable })
        $busy = @($running | Where-Object {
            $_.MainWindowTitle -match "responding|Sending|received the message"
        }).Count -gt 0
        if (-not $busy) { break }
        if ((Get-Date) -ge $deadline) { throw "Update timed out; the running app was left open." }
        Start-Sleep -Seconds 2
    } while ($true)

    foreach ($process in $running) {
        if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(30000)) {
            throw "HABITAT did not close normally; no process was terminated."
        }
    }
    foreach ($item in Get-ChildItem -LiteralPath $stagedRoot -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $targetRoot -Recurse -Force
    }
    if ((Get-AssemblyHash (Join-Path $stagedRoot "HABITAT.dll")) -ne
        (Get-AssemblyHash (Join-Path $targetRoot "HABITAT.dll"))) {
        throw "The installed assembly does not match the staged build."
    }
    Start-Process -FilePath "$env:WINDIR\System32\wscript.exe" `
        -ArgumentList (Join-Path $projectRoot "HABITAT.vbs") -WindowStyle Hidden
    Write-UpdateLog "Staged build installed and HABITAT relaunched."
} catch {
    Write-UpdateLog ("Update failed: " + $_.Exception.Message)
    throw
}
