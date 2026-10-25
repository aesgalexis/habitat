param(
    [string]$InstallDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\HABITAT'),
    [switch]$NoLaunch,
    [switch]$NoShortcuts
)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\', '/')
$destination = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\', '/')
if ($destination -eq $packageRoot -or
    $destination.StartsWith($packageRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $packageRoot.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Extract the package separately from the installation directory.'
}
if ((Test-Path -LiteralPath (Join-Path $destination 'unins000.exe')) -or
    (Test-Path -LiteralPath (Join-Path $destination 'HABITAT.exe'))) {
    throw 'An EXE installation already uses this directory. Uninstall it or choose a separate directory before installing the ZIP package.'
}
if (-not ('HabitatInstallerProfile' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HabitatInstallerProfile {
    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
}
'@
}
[uint32]$packageLength = 0
if ([HabitatInstallerProfile]::GetCurrentPackageFullName([ref]$packageLength, [IntPtr]::Zero) -ne 15700 -or
    [Environment]::GetFolderPath('LocalApplicationData') -match '[\\/]sandbox\.[^\\/]+[\\/]') {
    throw 'Install HABITAT from your normal Windows session, outside a packaged app or temporary sandbox.'
}
$version = (Get-Content -LiteralPath (Join-Path $packageRoot 'VERSION') -Raw).Trim()
$metadata = Get-Content -LiteralPath (Join-Path $packageRoot 'release.json') -Raw | ConvertFrom-Json
if ($metadata.version -ne $version -or $metadata.runtime -ne 'win-x64') { throw 'Invalid release metadata.' }
Import-Module (Join-Path $packageRoot 'scripts\Habitat.Release.psm1') -Force
$payload = Join-Path $packageRoot 'artifacts\staged\app'
Test-HabitatBuildManifest $payload
foreach ($required in 'HABITAT.Hotkey.exe', 'HABITAT.Hotkey.dll', 'Habitat.Activation.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Package is missing $required." }
}
# Reject filesystem aliases before copying or activating any release.
foreach ($root in $packageRoot, $destination) {
    Assert-HabitatNoLinks $root
}
foreach ($item in Get-ChildItem -LiteralPath $packageRoot -Recurse -Force) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package contents cannot contain filesystem links.' }
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in 'HABITAT.vbs', 'HABITAT-Hotkey.vbs', 'VERSION', 'release.json', 'README.md', 'CHANGELOG.md', 'LICENSE', 'docs', 'scripts', 'extension', 'vscode-extension') {
    Copy-Item -LiteralPath (Join-Path $packageRoot $name) -Destination $destination -Recurse -Force
}
$stage = Join-Path $destination ('artifacts\staged\install-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Get-ChildItem -LiteralPath $payload -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $stage -Recurse -Force }
if ($NoLaunch) {
    # Offline deployment / tests: no process, startup registry or user-data access.
    $installed = Install-HabitatRelease -ArtifactsRoot (Join-Path $destination 'artifacts') -StageDirectory $stage -BeforeActivate {
        param($previous)
        foreach ($name in 'HABITAT', 'HABITAT.Hotkey') {
            if (Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object {
                $_.Path -and $_.Path.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)
            }) { throw 'Close the installed app and shortcut component before using -NoLaunch.' }
        }
    }
    Write-Host "Verified release installed: $installed"
} else {
    & (Join-Path $destination 'scripts\apply-staged-update.ps1') -StageDirectory $stage
}
if (-not $NoShortcuts) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'HABITAT.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $env:WINDIR 'System32\wscript.exe'
    $shortcut.Arguments = '"' + (Join-Path $destination 'HABITAT.vbs') + '"'
    $shortcut.WorkingDirectory = $destination
    $active = Get-HabitatActiveDirectory (Join-Path $destination 'artifacts')
    $shortcut.IconLocation = Join-Path $active 'HABITAT.exe'
    $shortcut.Save()
}
Write-Host "HABITAT $version installed in $destination"
Write-Host 'Open Set up HABITAT from the app menu to connect Chrome and optional integrations.'
Write-Host 'Chrome extension: https://chromewebstore.google.com/detail/ennnldolgdbbkgmpjjpkafeajihpimii'
Write-Host "Manual installation folder (if the store listing is unavailable): $(Join-Path $destination 'extension')"
