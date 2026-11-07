param([string]$InstallDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/HABITAT'))
$ErrorActionPreference = 'Stop'
if ([Environment]::GetFolderPath('LocalApplicationData') -match '[\\/]sandbox\.[^\\/]+[\\/]') {
    throw 'Register generated storage from the normal Windows session.'
}
$installation = [IO.Path]::GetFullPath($InstallDirectory)
$executable = Join-Path $installation 'HABITAT.exe'
if (-not [IO.File]::Exists((Join-Path $installation 'unins000.exe')) -or
    [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductName -ne 'HABITAT') {
    throw 'Select the existing HABITAT EXE installation.'
}
Import-Module (Join-Path $PSScriptRoot 'Habitat.Storage.psm1') -Force
$profile = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HABITAT'
if (-not [IO.Directory]::Exists($profile)) { throw 'Open HABITAT once before registering generated storage.' }
[Habitat.Storage.GeneratedStorage]::RegisterRoot($installation,
    (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'), (Join-Path $profile 'generated-storage.json'))
Write-Output 'This HABITAT installation can now maintain the generated files of this repository.'
