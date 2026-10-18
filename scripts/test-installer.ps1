param([Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$smokeRepo = Split-Path -Parent $PSScriptRoot
$metadata = Get-Content -LiteralPath (Join-Path $smokeRepo 'packaging/metadata.json') -Raw | ConvertFrom-Json
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{$($metadata.AppId)}_is1"
if (Test-Path -LiteralPath $uninstallKey) { throw 'HABITAT is already installed for this user. Run this check in a clean Windows account or Windows Sandbox.' }
$smokeRoot = Join-Path $smokeRepo ('artifacts/installer-smoke/' + [guid]::NewGuid().ToString('N'))
$installation = Join-Path $smokeRoot 'installed'
New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
function Invoke-SilentInstaller([string]$Executable, [string[]]$InstallerArguments) {
    $process = Start-Process -FilePath $Executable -ArgumentList $InstallerArguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installer exited with $($process.ExitCode). Logs: $smokeRoot" }
}
function Test-InstalledPayload {
    $manifest = Get-Content -LiteralPath (Join-Path $installation 'build-manifest.json') -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.files) {
        $file = Join-Path $installation $entry.path
        if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "Installed payload mismatch: $($entry.path)"
        }
    }
    foreach ($name in 'HABITAT', 'HABITAT.Hotkey') {
        $runtime = Get-Content -LiteralPath (Join-Path $installation "$name.runtimeconfig.json") -Raw | ConvertFrom-Json
        if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks -or
            -not $runtime.runtimeOptions.includedFrameworks) { throw "$name requires an external .NET runtime." }
    }
    $registration = Get-ItemProperty -LiteralPath $uninstallKey
    if ($registration.DisplayName -ne $metadata.PackageName -or $registration.Publisher -ne $metadata.Publisher -or
        [IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\') -ne $installation.TrimEnd('\')) {
        throw 'Uninstall registration does not match the manifest metadata.'
    }
}
try {
    Invoke-SilentInstaller $InstallerPath @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', '/TASKS=""',
        ('/DIR="' + $installation + '"'), ('/LOG="' + (Join-Path $smokeRoot 'install.log') + '"'))
    Test-InstalledPayload
    # Same-version replacement exercises the upgrade path without launching HABITAT.
    Invoke-SilentInstaller $InstallerPath @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', '/TASKS=""',
        ('/DIR="' + $installation + '"'), ('/LOG="' + (Join-Path $smokeRoot 'upgrade.log') + '"'))
    Test-InstalledPayload
} finally {
    $uninstaller = Join-Path $installation 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        Invoke-SilentInstaller $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
            ('/LOG="' + (Join-Path $smokeRoot 'uninstall.log') + '"'))
    }
}
if ((Test-Path -LiteralPath $uninstallKey) -or (Test-Path -LiteralPath (Join-Path $installation 'HABITAT.exe'))) {
    throw 'Uninstall left the application or its registration behind.'
}
$zipInstallation = Join-Path $smokeRoot 'zip-installation'
New-Item -ItemType Directory -Path $zipInstallation -Force | Out-Null
$marker = Join-Path $zipInstallation 'HABITAT.vbs'
[IO.File]::WriteAllText($marker, 'synthetic ZIP launcher; never executed')
try {
    $blocked = Start-Process -FilePath $InstallerPath -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS',
        ('/DIR="' + $zipInstallation + '"'), ('/LOG="' + (Join-Path $smokeRoot 'mixed-installation.log') + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($blocked.ExitCode -eq 0 -or (Test-Path -LiteralPath (Join-Path $zipInstallation 'HABITAT.exe')) -or
        [IO.File]::ReadAllText($marker) -ne 'synthetic ZIP launcher; never executed') {
        throw 'Installer did not preserve and reject an existing ZIP installation.'
    }
} finally {
    $unexpectedUninstaller = Join-Path $zipInstallation 'unins000.exe'
    if (Test-Path -LiteralPath $unexpectedUninstaller) {
        Invoke-SilentInstaller $unexpectedUninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    }
}
if (Test-Path -LiteralPath $uninstallKey) { throw 'Mixed installation check left an uninstall registration.' }
Write-Host "Silent install, same-version upgrade, complete payload, self-contained runtimes, uninstall and ZIP installation protection passed. Logs: $smokeRoot"
