param(
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [string]$ReferenceDirectory,
    [string]$ExpectedDriveClientPath,
    [switch]$RequireDrive,
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$payloadRepo = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $payloadRepo '.dotnet-local'
$env:NUGET_PACKAGES = Join-Path $payloadRepo '.nuget/packages'
$payloadArguments = @('--payload', [IO.Path]::GetFullPath($PayloadDirectory))
if ($ReferenceDirectory) { $payloadArguments += @('--match', [IO.Path]::GetFullPath($ReferenceDirectory)) }
if ($ExpectedDriveClientPath) { $payloadArguments += @('--expected-drive', [IO.Path]::GetFullPath($ExpectedDriveClientPath)) }
if ($RequireDrive) { $payloadArguments += '--require-drive' }
if ($env:HABITAT_RELEASE_VERIFIER) {
    dotnet $env:HABITAT_RELEASE_VERIFIER @payloadArguments
} else {
    $verifierOutput = Join-Path $payloadRepo 'artifacts/release-verifier/'
    $verifierArguments = @('--project', (Join-Path $payloadRepo 'tools/Habitat.Release.Verify'), '--configuration', 'Release',
        "-p:BaseOutputPath=$verifierOutput")
    if ($NoRestore) { $verifierArguments += '--no-restore' }
    dotnet run @verifierArguments -- @payloadArguments
}
if ($LASTEXITCODE -ne 0) { throw 'Release payload capabilities or binary identity do not match.' }
