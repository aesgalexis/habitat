param([switch]$Install, [string]$OutputDirectory)
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetHome = Join-Path $projectRoot ".dotnet-local"
$nugetPackages = Join-Path $projectRoot ".nuget\packages"
$project = Join-Path $projectRoot "src\Habitat.App\Habitat.App.csproj"
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$output = if ($OutputDirectory) {
    [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $projectRoot $OutputDirectory }))
}
    else { Join-Path $artifactsRoot ("staged\build-" + [guid]::NewGuid().ToString("N")) }
if (-not $output.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build output must stay inside this project artifacts directory.'
}
if (Test-Path -LiteralPath $output) { throw 'Build output must be a new directory.' }
Import-Module (Join-Path $PSScriptRoot 'Habitat.Storage.psm1') -Force
[IO.Directory]::CreateDirectory($artifactsRoot) | Out-Null
$buildOperation = [Habitat.Storage.GeneratedStorage]::BeginOperation($artifactsRoot, @($output))
$publishFinished = $false
$buildVerified = $false
try {

$env:DOTNET_CLI_HOME = $dotnetHome
$env:NUGET_PACKAGES = $nugetPackages

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:HabitatUiChecks=false `
    --output $output
$publishFinished = $true

if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar HABITAT."
}

Import-Module (Join-Path $PSScriptRoot 'Habitat.Release.psm1') -Force
New-HabitatBuildManifest $output
Test-HabitatBuildManifest $output
$buildVerified = $true
Write-Host "Verified HABITAT build prepared at $output"
if ($Install) {
    & (Join-Path $PSScriptRoot 'apply-staged-update.ps1') -StageDirectory $output -DeferStorageMaintenance
}
} finally {
    if ($publishFinished -and -not $buildVerified -and [IO.Directory]::Exists($output)) {
        Complete-HabitatGeneratedOutput $artifactsRoot $output 'build'
    }
    $buildOperation.Dispose()
}
try { $null = Invoke-HabitatStorageMaintenance -ArtifactsRoot $artifactsRoot -Clean -Automatic }
catch { Write-Warning 'Generated storage maintenance could not finish. Build output was retained.' }
if (-not $Install) {
    Write-Host "To activate from your normal Windows session: scripts\apply-staged-update.ps1 -StageDirectory '$output'"
}
