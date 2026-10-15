$ErrorActionPreference = "Stop"
$taskProjectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $taskProjectRoot ".dotnet-local"
$env:NUGET_PACKAGES = Join-Path $taskProjectRoot ".nuget\packages"
$taskOutput = Join-Path $taskProjectRoot "artifacts\tasks-ui-checks"
dotnet publish (Join-Path $taskProjectRoot "src\Habitat.App\Habitat.App.csproj") `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:Platform=x64 -p:HabitatUiChecks=true -p:PublishReadyToRun=false --output $taskOutput
if ($LASTEXITCODE -ne 0) { throw "Could not build the tasks UI checks." }
$taskProcess = Start-Process -FilePath (Join-Path $taskOutput "HABITAT.exe") `
    -WorkingDirectory $taskOutput -WindowStyle Hidden -PassThru
if (-not $taskProcess.WaitForExit(30000)) {
    Stop-Process -Id $taskProcess.Id
    throw "Tasks UI checks timed out."
}
Get-Content -LiteralPath (Join-Path $taskOutput "tasks-ui-checks.log")
if ($taskProcess.ExitCode -ne 0) { throw "Tasks UI checks failed with exit code $($taskProcess.ExitCode)." }
