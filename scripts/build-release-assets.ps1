param(
    [Parameter(Mandatory)][string]$DriveClientPath,
    [string]$InnoCompilerPath,
    [ValidateRange(1, 1800)][int]$TimeoutSeconds = 360
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $repo 'VERSION') -Raw).Trim()
$client = (Resolve-Path -LiteralPath $DriveClientPath).Path
$stage = Join-Path $repo ('artifacts/staged/release-' + [guid]::NewGuid().ToString('N'))
$artifacts = Join-Path $repo 'artifacts'
$work = Join-Path $artifacts ('verification/run-' + [guid]::NewGuid().ToString('N'))
$logs = Join-Path $work 'release-logs'
$watch = [Diagnostics.Stopwatch]::StartNew()
$previousClient = $env:HabitatDriveOAuthClientFile
$previousVerifier = $env:HABITAT_RELEASE_VERIFIER
$previousMaintenance = $env:HABITAT_STORAGE_SKIP_AUTOMATIC
$env:DOTNET_CLI_HOME = Join-Path $repo '.dotnet-local'
$env:NUGET_PACKAGES = Join-Path $repo '.nuget/packages'
$windowsPowerShell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
Import-Module (Join-Path $PSScriptRoot 'Habitat.Process.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Habitat.Storage.psm1') -Force
[IO.Directory]::CreateDirectory($artifacts) | Out-Null
$releaseOperation = [Habitat.Storage.GeneratedStorage]::BeginOperation($artifacts, @($stage, $work))
function Invoke-ReleasePhase([string]$Name, [string]$Program, [string[]]$Arguments, [int]$Limit) {
    if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw "Release budget exhausted before $Name." }
    $check = Start-HabitatProcess -Name $Name -FilePath $Program -Arguments $Arguments -LogDirectory $logs -TimeoutSeconds $Limit
    Wait-HabitatProcesses -Checks @($check) -Budget $watch -BudgetSeconds $TimeoutSeconds
}
function Get-ScriptArguments([string]$Name, [string[]]$Parameters) {
    @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot $Name)) + $Parameters
}
Push-Location $repo
try {
    # The jobs share this tree. Sweep it once after both readers have finished.
    $env:HABITAT_STORAGE_SKIP_AUTOMATIC = '1'
    try {
        $env:HabitatDriveOAuthClientFile = $client
        Invoke-ReleasePhase 'shared-release-build' $windowsPowerShell (Get-ScriptArguments 'build-winui.ps1' @('-OutputDirectory', $stage)) 240
    } finally { $env:HabitatDriveOAuthClientFile = $previousClient }
    Write-Host ('Shared release build completed in {0:N1}s.' -f $watch.Elapsed.TotalSeconds)

    $verifierOutput = Join-Path $work 'verifier/'
    Invoke-ReleasePhase 'build-payload-verifier' 'dotnet' @('build', 'tools/Habitat.Release.Verify',
        '--configuration', 'Release', "-p:BaseOutputPath=$verifierOutput") 90
    $env:HABITAT_RELEASE_VERIFIER = Join-Path $verifierOutput 'Release/net10.0/Habitat.Release.Verify.dll'
    Invoke-ReleasePhase 'verify-shared-payload' $windowsPowerShell (Get-ScriptArguments 'check-release-payload.ps1' @(
        '-PayloadDirectory', $stage, '-RequireDrive', '-ExpectedDriveClientPath', $client)) 60

    $checks = @()
    try {
        foreach ($format in 'ZIP', 'EXE') {
            $parameters = @('-Format', $format, '-StageDirectory', $stage, '-Version', $version)
            if ($format -eq 'EXE' -and $InnoCompilerPath) { $parameters += @('-InnoCompilerPath', $InnoCompilerPath) }
            $commandLine = Get-ScriptArguments 'verify-release-format.ps1' $parameters
            $checks += Start-HabitatProcess -Name $format -FilePath $windowsPowerShell -Arguments $commandLine -LogDirectory $logs -TimeoutSeconds 180
        }
    } catch {
        foreach ($check in $checks) { Close-HabitatProcess $check }
        throw
    }
    Wait-HabitatProcesses -Checks $checks -Budget $watch -BudgetSeconds $TimeoutSeconds
    Complete-HabitatGeneratedOutput $artifacts $stage 'consumed'
    Complete-HabitatGeneratedOutput $artifacts $work 'test'
} finally {
    $env:HABITAT_RELEASE_VERIFIER = $previousVerifier
    $env:HABITAT_STORAGE_SKIP_AUTOMATIC = $previousMaintenance
    $releaseOperation.Dispose()
    Pop-Location
}
try { $null = Invoke-HabitatStorageMaintenance -ArtifactsRoot $artifacts -Clean -Automatic }
catch { Write-Warning 'Generated storage maintenance could not finish. Release assets were retained.' }
Write-Host ('Release assets ready in {0:N1}s.' -f $watch.Elapsed.TotalSeconds)
