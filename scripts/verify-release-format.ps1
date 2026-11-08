param(
    [Parameter(Mandatory)][ValidateSet('ZIP', 'EXE')][string]$Format,
    [Parameter(Mandatory)][string]$StageDirectory,
    [Parameter(Mandatory)][string]$Version,
    [string]$InnoCompilerPath
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repo
if ($Format -eq 'ZIP') {
    ./scripts/package-release.ps1 -StageDirectory $StageDirectory -RequireDrive
    ./tests/package.checks.ps1 -PackagePath "artifacts/packages/HABITAT-$Version-win-x64.zip" -ReferenceDirectory $StageDirectory -RequireDrive
} else {
    ./scripts/build-installer.ps1 -Version $Version -StageDirectory $StageDirectory -RequireDrive -InnoCompilerPath $InnoCompilerPath
    ./scripts/test-installer.ps1 -InstallerPath "artifacts/distribution/$Version/HABITAT-$Version-x64-setup.exe" -ReferenceDirectory $StageDirectory -RequireDrive
}
