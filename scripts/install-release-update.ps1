param([Parameter(Mandatory)][string]$RequestPath)
# Use this host's built-in modules even when an older launcher passes
# PowerShell 7 paths or a customised user module path.
$env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')
$ErrorActionPreference = 'Stop'
$request = $null
$appExited = $false
$work = [IO.Path]::GetFullPath($PSScriptRoot)
$resultPath = Join-Path (Split-Path -Parent $work) 'last-result.json'
$diagnosticDirectory = Join-Path (Split-Path -Parent $work) 'diagnostics'
$diagnosticPath = Join-Path $diagnosticDirectory ((Split-Path -Leaf $work) + '.jsonl')
$phase = 'request-read'

function Protect-UpdateDiagnostic([string]$Text) {
    $text = [regex]::Replace($Text, 'https?://[^\s''"<>]+', '<url>', 'IgnoreCase')
    $text = [regex]::Replace($text, '[A-Za-z]:[\\/][^\r\n''"<>]*|\\\\[^\r\n''"<>]+', '<path>')
    $text = [regex]::Replace($text, '[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}', '<email>')
    $text = [regex]::Replace($text, '(?i)(authorization|password|token|secret|key)["'']*\s*[:=]\s*(["'']).*?\2', '$1=<redacted>')
    $text = [regex]::Replace($text, '(?i)(authorization|password|token|secret|key)["'' ]*[:=]["'' ]*[^,\s}]+', '$1=<redacted>')
    if ($text.Length -gt 4096) { $text = $text.Substring(0, 4096) + ' [truncated]' }
    return $text
}
function Write-UpdatePhase([string]$Phase, $Failure = $null) {
    $script:phase = $Phase
    try {
        $entry = [ordered]@{ timestamp = [DateTime]::UtcNow.ToString('O'); phase = $Phase }
        if ($Failure) {
            $entry.exception = $Failure.Exception.GetType().FullName
            $entry.hresult = $Failure.Exception.HResult
            $entry.message = Protect-UpdateDiagnostic $Failure.Exception.Message
            $entry.line = $Failure.InvocationInfo.ScriptLineNumber
        }
        [IO.Directory]::CreateDirectory($diagnosticDirectory) | Out-Null
        [IO.File]::AppendAllText($diagnosticPath, (($entry | ConvertTo-Json -Compress) + [Environment]::NewLine))
    } catch { } # Diagnostics must not change the installation outcome.
}

function Write-UpdateResult([bool]$Succeeded) {
    $result = @{ succeeded = $Succeeded; version = $request.version; timestamp = [DateTime]::UtcNow.ToString('O') }
    [IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}
function Start-UpdateRelease([string]$Directory) {
    $exe = Join-Path $Directory 'HABITAT.exe'
    $started = Start-Process -FilePath $exe -WorkingDirectory $Directory -WindowStyle Hidden -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(30)
        do {
            if ($started.HasExited) { throw 'The updated application exited before becoming ready.' }
            try {
                $pair = Invoke-RestMethod -Uri 'http://127.0.0.1:8765/pair' -Headers @{ 'X-Habitat-Pair' = '1' } -TimeoutSec 2
                $status = Invoke-RestMethod -Uri 'http://127.0.0.1:8765/status' -Headers @{ 'X-Habitat-Key' = $pair.key } -TimeoutSec 2
                if ($status.processId -eq $started.Id -and $status.protocolVersion -ge 1) { return }
            } catch { }
            Start-Sleep -Milliseconds 300
        } while ((Get-Date) -lt $deadline)
        throw 'The updated application did not become ready.'
    } finally { $started.Dispose() }
}
function Stop-UpdateRelease([string]$Directory) {
    foreach ($process in @(Get-Process -Name HABITAT -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -eq (Join-Path $Directory 'HABITAT.exe')
    })) {
        if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(15000)) {
            Stop-Process -Id $process.Id -ErrorAction Stop
            if (-not $process.WaitForExit(5000)) { throw 'The failed application could not stop.' }
        }
    }
}
try {
    Write-UpdatePhase 'request-read'
    if ([IO.Path]::GetFullPath($RequestPath) -ne (Join-Path $work 'request.json')) { throw 'Invalid update request location.' }
    $request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HabitatReleaseUpdateProfile {
    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
}
'@
    [uint32]$length = 0
    if ([HabitatReleaseUpdateProfile]::GetCurrentPackageFullName([ref]$length, [IntPtr]::Zero) -ne 15700 -or
        [Environment]::GetFolderPath('LocalApplicationData') -match '[\\/]sandbox\.[^\\/]+[\\/]') {
        throw 'Updates require the normal Windows session.'
    }
    Write-UpdatePhase 'modules'
    Import-Module (Join-Path $work 'Habitat.Release.psm1') -Force
    Import-Module (Join-Path $work 'Habitat.Update.psm1') -Force
    Write-UpdatePhase 'paths'
    $root = [IO.Path]::GetFullPath($request.installRoot).TrimEnd('\', '/')
    $currentExe = Assert-HabitatUpdatePath $root $request.currentExe
    $package = Assert-HabitatUpdatePath $work $request.packagePath
    Write-UpdatePhase 'package-integrity'
    if ($request.version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or
        $request.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $request.sha256) { throw 'The update package failed verification.' }
    if ($request.kind -eq 'Installer') {
        if ($currentExe -ne (Join-Path $root 'HABITAT.exe') -or -not (Test-Path -LiteralPath (Join-Path $root 'unins000.exe')) -or
            [IO.Path]::GetFileName($package) -cne "HABITAT-$($request.version)-x64-setup.exe") { throw 'Invalid EXE installation.' }
    } elseif ($request.kind -eq 'Zip') {
        $active = Get-HabitatActiveDirectory (Join-Path $root 'artifacts')
        if ($currentExe -ne (Join-Path $active 'HABITAT.exe') -or -not (Test-Path -LiteralPath (Join-Path $root 'HABITAT.vbs'))) { throw 'The active ZIP installation changed.' }
        $extracted = Assert-HabitatUpdatePath $work $request.extractedDirectory
        Write-UpdatePhase 'payload-verify'
        Test-HabitatBuildManifest (Join-Path $extracted 'artifacts/staged/app')
    } else { throw 'Unsupported update installation.' }
    Write-UpdatePhase 'parent-identity'
    $parent = Get-Process -Id $request.processId -ErrorAction Stop
    if ($parent.Path -ne $currentExe -or $parent.StartTime.ToUniversalTime().Ticks -ne [long]$request.processStartTicks) { throw 'The original application changed.' }
    [IO.File]::WriteAllText((Join-Path $work 'ready'), 'ready')
    Write-UpdatePhase 'wait-parent'
    if (-not $parent.WaitForExit(120000)) { throw 'HABITAT did not close. The update was not applied.' }
    $appExited = $true
    Write-UpdatePhase 'stop-companion'
    $companion = Join-Path (Split-Path -Parent $currentExe) 'HABITAT.Hotkey.exe'
    if (Test-Path -LiteralPath $companion) {
        $stop = Start-Process -FilePath $companion -ArgumentList '--stop' -WindowStyle Hidden -PassThru
        try { if (-not $stop.WaitForExit(15000) -or $stop.ExitCode -ne 0) { throw 'The shortcut component could not stop.' } }
        finally { $stop.Dispose() }
    }
    if ($request.kind -eq 'Installer') {
        Write-UpdatePhase 'installer'
        $arguments = Get-HabitatInstallerArguments $root (Join-Path $work 'installer.log')
        $setup = Start-Process -FilePath $package -ArgumentList $arguments -WindowStyle Hidden -PassThru
        try { if (-not $setup.WaitForExit(300000) -or $setup.ExitCode -ne 0) { throw 'The installer could not finish.' } }
        finally { $setup.Dispose() }
        Write-UpdatePhase 'startup'
        Start-UpdateRelease $root
    } else {
        Write-UpdatePhase 'activation'
        $null = Install-HabitatZipUpdate -PackageRoot $extracted -InstallRoot $root -Version $request.version `
            -BeforeActivate {
                param($previous)
                if (Get-Process -Name HABITAT -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $previous 'HABITAT.exe') }) {
                    throw 'HABITAT reopened before installation. Try again.'
                }
            } -StartRelease { param($directory) Write-UpdatePhase 'startup'; Start-UpdateRelease $directory } `
              -StopRelease { param($directory) Write-UpdatePhase 'rollback'; Stop-UpdateRelease $directory }
    }
    Write-UpdateResult $true
    Write-UpdatePhase 'complete'
} catch {
    $failure = $_
    Write-UpdatePhase $phase $failure
    @{ phase = $phase; exception = $failure.Exception.GetType().FullName;
        message = (Protect-UpdateDiagnostic $failure.Exception.Message) } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath (Join-Path $work 'update-error.log') -Encoding UTF8
    if ($request) { Write-UpdateResult $false }
    if ($appExited -and (Test-Path -LiteralPath $currentExe)) {
        # ZIP activation already restores its pointer on failure. Reopening the
        # original executable also covers failures before activation and EXE errors.
        $alreadyOpen = Get-Process -Name HABITAT -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $currentExe }
        if (-not $alreadyOpen) { Start-Process -FilePath $currentExe -WorkingDirectory (Split-Path -Parent $currentExe) -WindowStyle Hidden }
    }
    exit 1
}
