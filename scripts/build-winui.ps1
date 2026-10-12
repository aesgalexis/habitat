$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetHome = Join-Path $projectRoot ".dotnet-local"
$nugetPackages = Join-Path $projectRoot ".nuget\packages"
$project = Join-Path $projectRoot "src\Habitat.App\Habitat.App.csproj"
$output = Join-Path $projectRoot "artifacts\win-x64"

$env:DOTNET_CLI_HOME = $dotnetHome
$env:NUGET_PACKAGES = $nugetPackages

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    --output $output

if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar HABITAT."
}

Write-Host "HABITAT listo en $output\HABITAT.exe"
