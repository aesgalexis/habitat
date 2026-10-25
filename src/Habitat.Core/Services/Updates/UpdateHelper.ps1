param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanPath -Raw -Encoding UTF8 | ConvertFrom-Json
$work = Split-Path -Parent ([IO.Path]::GetFullPath($PlanPath))
$log = Join-Path $work 'install.log'
$parentExited = $false
function Start-HabitatAfterUpdate {
    if ($plan.Kind -eq 'zip') {
        Start-Process -FilePath "$env:WINDIR\System32\wscript.exe" -ArgumentList ('"' + $plan.Launcher + '"') -WindowStyle Hidden
    } else {
        Start-Process -FilePath $plan.Executable -WorkingDirectory $plan.Directory -WindowStyle Hidden
    }
}
try {
    if ($plan.Kind -notin 'exe', 'zip' -or $plan.Version -notmatch '^\d+\.\d+\.\d+$' -or
        $plan.Sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid update plan.' }
    $package = [IO.Path]::GetFullPath($plan.Package)
    if ((Split-Path -Parent $package) -ne $work -or
        (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $plan.Sha256) { throw 'Package integrity verification failed.' }
    $parent = Get-Process -Id $plan.ParentId -ErrorAction SilentlyContinue
    if ($parent -and $parent.Path -ne $plan.Executable) { throw 'Update parent identity changed.' }
    [IO.File]::WriteAllText((Join-Path $work 'helper-ready'), 'ready')
    if ($parent) {
        $deadline = [DateTime]::UtcNow.AddSeconds(120)
        while (-not $parent.WaitForExit(200)) {
            # Cancellation retires the owned download directory. Never install
            # after a cancelled or unsuccessful preparation in the still-open app.
            if (-not (Test-Path -LiteralPath $package)) { exit 0 }
            if ([DateTime]::UtcNow -ge $deadline) { throw 'HABITAT did not finish closing. No update was installed.' }
        }
    }
    $parentExited = $true
    $companion = Join-Path (Split-Path -Parent $plan.Executable) 'HABITAT.Hotkey.exe'
    if (Get-Process -Name HABITAT.Hotkey -ErrorAction SilentlyContinue | Where-Object Path -EQ $companion) {
        $stop = Start-Process -FilePath $companion -ArgumentList '--stop' -WindowStyle Hidden -PassThru
        if (-not $stop.WaitForExit(15000) -or $stop.ExitCode -ne 0) { throw 'The shortcut component did not stop.' }
    }
    # Verify again after waiting for the parent. Nothing unverified is executed.
    if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $plan.Sha256) { throw 'The package changed before installation.' }
    if ($plan.Kind -eq 'exe') {
        $install = Start-Process -FilePath $package -WindowStyle Hidden -PassThru -ArgumentList @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOCLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS',
            ('/DIR="' + $plan.Directory + '"'), ('/LOG="' + $log + '"'))
        # Do not restart over an installer that is still replacing binaries.
        $install.WaitForExit()
        if ($install.ExitCode -ne 0) { throw 'The installer did not complete successfully.' }
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $plan.Directory 'HABITAT.dll')).FileVersion
        if ($version -ne ($plan.Version + '.0')) { throw 'The installed version differs from the downloaded update.' }
        Start-HabitatAfterUpdate
    } else {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $unpacked = Join-Path $work 'package'
        $archive = [IO.Compression.ZipFile]::OpenRead($package)
        try {
            [long]$total = 0
            if ($archive.Entries.Count -gt 20000) { throw 'The update archive is too large.' }
            foreach ($entry in $archive.Entries) {
                $target = [IO.Path]::GetFullPath((Join-Path $unpacked $entry.FullName))
                $total += $entry.Length
                if (-not $target.StartsWith($unpacked + '\', [StringComparison]::OrdinalIgnoreCase) -or
                    $total -gt 2GB -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
                    throw 'The update archive contains an invalid path or entry.'
                }
            }
        } finally { $archive.Dispose() }
        [IO.Compression.ZipFile]::ExtractToDirectory($package, $unpacked)
        $metadata = Get-Content -LiteralPath (Join-Path $unpacked 'release.json') -Raw | ConvertFrom-Json
        if ($metadata.version -ne $plan.Version -or $metadata.runtime -ne 'win-x64') { throw 'Unexpected ZIP update metadata.' }
        # The verified package retains the ZIP updater's preparation checks,
        # manifests, previous-release retention and startup rollback.
        & (Join-Path $unpacked 'Install-HABITAT.ps1') -InstallDirectory $plan.Directory -NoShortcuts
    }
} catch {
    [IO.File]::WriteAllText((Join-Path $work 'helper-failed'), 'failed')
    $_ | Out-String | Add-Content -LiteralPath $log
    @{ Failed = $true; Version = $plan.Version } | ConvertTo-Json | Set-Content -LiteralPath $plan.ResultPath -Encoding UTF8
    if ($parentExited) { Start-HabitatAfterUpdate }
    exit 1
}
