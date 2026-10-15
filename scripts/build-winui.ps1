param([switch]$Install)
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetHome = Join-Path $projectRoot ".dotnet-local"
$nugetPackages = Join-Path $projectRoot ".nuget\packages"
$project = Join-Path $projectRoot "src\Habitat.App\Habitat.App.csproj"
$output = Join-Path $projectRoot ("artifacts\staged\build-" + [guid]::NewGuid().ToString("N"))

$env:DOTNET_CLI_HOME = $dotnetHome
$env:NUGET_PACKAGES = $nugetPackages

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:HabitatUiChecks=false `
    --output $output

if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar HABITAT."
}

Import-Module (Join-Path $PSScriptRoot 'Habitat.Release.psm1') -Force
New-HabitatBuildManifest $output
Test-HabitatBuildManifest $output
Write-Host "Verified HABITAT build prepared at $output"
if ($Install) {
    & (Join-Path $PSScriptRoot 'apply-staged-update.ps1') -StageDirectory $output
} else {
    Write-Host "To activate from your normal Windows session: scripts\apply-staged-update.ps1 -StageDirectory '$output'"
}
