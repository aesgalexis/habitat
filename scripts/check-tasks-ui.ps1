param([switch]$VSCodeOnly, [switch]$ConversationOnly, [switch]$UpdatesOnly, [switch]$FacebookOnly, [switch]$ContactDestinationsOnly, [switch]$ContactSourcesOnly, [switch]$ContactChannelsOnly, [switch]$PhoneLinkOnly, [switch]$HotkeyOnly, [switch]$WindowMenuOnly, [switch]$CodexSchedulesOnly, [switch]$ScrollEdgesOnly, [switch]$PreferencesOnly, [string]$OutputDirectory, [ValidateRange(1,600)][int]$TimeoutSeconds = 180)
$ErrorActionPreference = "Stop"
$taskProjectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $taskProjectRoot ".dotnet-local"
$env:NUGET_PACKAGES = Join-Path $taskProjectRoot ".nuget\packages"
$taskArtifacts = [IO.Path]::GetFullPath((Join-Path $taskProjectRoot 'artifacts'))
$taskOutput = if ($OutputDirectory) {
        $requestedOutput = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $taskProjectRoot $OutputDirectory }
        [IO.Path]::GetFullPath($requestedOutput)
    }
    else { Join-Path $taskArtifacts ('ui-checks/run-' + [guid]::NewGuid().ToString('N')) }
if (-not $taskOutput.StartsWith($taskArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'UI check output must stay inside this project artifacts directory.'
}
Import-Module (Join-Path $PSScriptRoot 'Habitat.Storage.psm1') -Force
[IO.Directory]::CreateDirectory($taskArtifacts) | Out-Null
$uiOperation = [Habitat.Storage.GeneratedStorage]::BeginOperation($taskArtifacts, @($taskOutput))
try {
$buildWatch = [Diagnostics.Stopwatch]::StartNew()
dotnet publish (Join-Path $taskProjectRoot "src\Habitat.App\Habitat.App.csproj") `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:Platform=x64 -p:HabitatUiChecks=true -p:PublishReadyToRun=false "-p:BaseOutputPath=$taskOutput/build/" --output $taskOutput
if ($LASTEXITCODE -ne 0) { throw "Could not build the tasks UI checks." }
Write-Host ('WinUI build completed in {0:N1}s.' -f $buildWatch.Elapsed.TotalSeconds)
function Invoke-HabitatUiCheckProcess([string]$Mode, [string]$LogName) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $taskProcess = Start-Process -FilePath (Join-Path $taskOutput 'HABITAT.exe') -ArgumentList $Mode `
        -WorkingDirectory $taskOutput -WindowStyle Hidden -PassThru
    if (-not $taskProcess.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $taskProcess.Id
        throw "Native UI checks timed out: $Mode."
    }
    Get-Content -LiteralPath (Join-Path $taskOutput $LogName)
    if ($taskProcess.ExitCode -ne 0) { throw "Native UI checks failed: $Mode (exit $($taskProcess.ExitCode))." }
    Write-Host ('{0} completed in {1:N1}s.' -f $Mode, $watch.Elapsed.TotalSeconds)
}
$checkMode = if ($PreferencesOnly) { '--preferences-ui-only' } elseif ($ContactDestinationsOnly) { '--contact-destinations-ui-only' } elseif ($ContactChannelsOnly) { '--contact-channels-ui-only' } elseif ($ContactSourcesOnly) { '--contact-sources-ui-only' } elseif ($PhoneLinkOnly) { '--phone-link-ui-only' } elseif ($FacebookOnly) { '--facebook-ui-only' } elseif ($UpdatesOnly) { '--updates-ui-only' } elseif ($ConversationOnly) { '--conversation-ui-only' } elseif ($WindowMenuOnly) { '--window-menu-ui-only' } elseif ($ScrollEdgesOnly) { '--scroll-edges-ui-only' } elseif ($CodexSchedulesOnly) { '--codex-schedules-ui-only' } elseif ($HotkeyOnly) { '--hotkey-ui-only' } elseif ($VSCodeOnly) { '--vscode-ui-only' } else { '--all-ui-checks' }
Invoke-HabitatUiCheckProcess $checkMode $(if ($WindowMenuOnly) { 'window-menu-checks.log' } else { 'tasks-ui-checks.log' })
if ($checkMode -eq '--all-ui-checks') {
    # MainWindow has a separate host/closing lifecycle. Reuse this binary to
    # verify it too, rather than leaving the window-menu-only path outside CI.
    Invoke-HabitatUiCheckProcess '--window-menu-ui-only' 'window-menu-checks.log'
}
if (-not $OutputDirectory) { Complete-HabitatGeneratedOutput $taskArtifacts $taskOutput 'test' }
} finally { $uiOperation.Dispose() }
try { $null = Invoke-HabitatStorageMaintenance -ArtifactsRoot $taskArtifacts -Clean -Automatic }
catch { Write-Warning 'Generated storage maintenance could not finish. UI check output was retained.' }
