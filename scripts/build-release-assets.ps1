param(
    [Parameter(Mandatory)][string]$DriveClientPath,
    [string]$InnoCompilerPath
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $repo 'VERSION') -Raw).Trim()
$client = (Resolve-Path -LiteralPath $DriveClientPath).Path
$stage = Join-Path $repo ('artifacts/staged/release-' + [guid]::NewGuid().ToString('N'))
$watch = [Diagnostics.Stopwatch]::StartNew()
$previousClient = $env:HabitatDriveOAuthClientFile
try {
    $env:HabitatDriveOAuthClientFile = $client
    & (Join-Path $PSScriptRoot 'build-winui.ps1') -OutputDirectory $stage
} finally {
    $env:HabitatDriveOAuthClientFile = $previousClient
}
Write-Host ('Shared release build completed in {0:N1}s.' -f $watch.Elapsed.TotalSeconds)

$previousVerifier = $env:HABITAT_RELEASE_VERIFIER
$jobs = @()
try {
    $verifierOutput = Join-Path $repo 'artifacts/release-verifier/'
    dotnet build (Join-Path $repo 'tools/Habitat.Release.Verify') --configuration Release "-p:BaseOutputPath=$verifierOutput"
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the release verifier.' }
    $env:HABITAT_RELEASE_VERIFIER = Join-Path $verifierOutput 'Release/net10.0/Habitat.Release.Verify.dll'
    & (Join-Path $PSScriptRoot 'check-release-payload.ps1') -PayloadDirectory $stage -RequireDrive -ExpectedDriveClientPath $client

    $jobs += Start-Job -Name ZIP -ArgumentList $repo, $stage, $version -ScriptBlock {
        param($repo, $stage, $version)
        $ErrorActionPreference = 'Stop'
        Set-Location -LiteralPath $repo
        $watch = [Diagnostics.Stopwatch]::StartNew()
        ./scripts/package-release.ps1 -StageDirectory $stage -RequireDrive
        ./tests/package.checks.ps1 -PackagePath "artifacts/packages/HABITAT-$version-win-x64.zip" -ReferenceDirectory $stage -RequireDrive
        Write-Host ('ZIP packaging and installation checks completed in {0:N1}s.' -f $watch.Elapsed.TotalSeconds)
    }
    $jobs += Start-Job -Name EXE -ArgumentList $repo, $stage, $version, $InnoCompilerPath -ScriptBlock {
        param($repo, $stage, $version, $compiler)
        $ErrorActionPreference = 'Stop'
        Set-Location -LiteralPath $repo
        $watch = [Diagnostics.Stopwatch]::StartNew()
        ./scripts/build-installer.ps1 -Version $version -StageDirectory $stage -RequireDrive -InnoCompilerPath $compiler
        ./scripts/test-installer.ps1 -InstallerPath "artifacts/distribution/$version/HABITAT-$version-x64-setup.exe" -ReferenceDirectory $stage -RequireDrive
        Write-Host ('EXE packaging and installation checks completed in {0:N1}s.' -f $watch.Elapsed.TotalSeconds)
    }
    $jobs | Wait-Job | Out-Null
    foreach ($job in $jobs) { Receive-Job -Job $job -ErrorAction Continue }
    $failed = @($jobs | Where-Object State -ne 'Completed')
    if ($failed) { throw ('Release packaging failed: ' + ($failed.Name -join ', ')) }
    Write-Host ('Release assets ready in {0:N1}s.' -f $watch.Elapsed.TotalSeconds)
} finally {
    $jobs | Remove-Job -Force
    $env:HABITAT_RELEASE_VERIFIER = $previousVerifier
}
