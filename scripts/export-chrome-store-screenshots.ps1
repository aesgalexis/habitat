$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'artifacts/chrome-store/screenshot-runtime'
$env:DOTNET_CLI_HOME = Join-Path $repo '.dotnet-local'
$env:NUGET_PACKAGES = Join-Path $repo '.nuget/packages'
dotnet publish (Join-Path $repo 'src/Habitat.App/Habitat.App.csproj') --configuration Release --runtime win-x64 `
    --self-contained true -p:Platform=x64 -p:HabitatUiChecks=true -p:PublishReadyToRun=false --output $output
if ($LASTEXITCODE -ne 0) { throw 'Could not compile the isolated screenshot exporter.' }
$process = Start-Process -FilePath (Join-Path $output 'HABITAT.exe') -ArgumentList '--chrome-store-assets' `
    -WorkingDirectory $output -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(30000)) {
    Stop-Process -Id $process.Id
    throw 'Screenshot export timed out.'
}
Get-Content -LiteralPath (Join-Path $output 'tasks-ui-checks.log')
if ($process.ExitCode -ne 0) { throw 'Screenshot export failed.' }
$assets = Join-Path $repo 'packaging/chrome-store'
foreach ($name in 'google-start-1280x800.png', 'google-results-1280x800.png') {
    Copy-Item -LiteralPath (Join-Path $output $name) -Destination (Join-Path $assets $name) -Force
}
Write-Host "Chrome Web Store screenshots: $assets"
