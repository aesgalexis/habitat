param(
    [switch]$SkipBuild,
    [switch]$NoRestore,
    [switch]$SkipUi,
    [ValidateSet('Full', 'Docs', 'Extensions')][string]$CheckProfile = 'Full',
    [ValidateRange(1, 1800)][int]$TimeoutSeconds = 360,
    [ValidateRange(1, 900)][int]$SuiteTimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$verifyRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $verifyRoot '.dotnet-local'
$env:NUGET_PACKAGES = Join-Path $verifyRoot '.nuget\packages'
$verifyArtifacts = Join-Path $verifyRoot 'artifacts'
$verifyOutput = Join-Path $verifyArtifacts ('verification/run-' + [guid]::NewGuid().ToString('N'))
$verifyLogs = Join-Path $verifyOutput 'logs'
$verifyWatch = [Diagnostics.Stopwatch]::StartNew()
$previousVerifier = $env:HABITAT_RELEASE_VERIFIER
Import-Module (Join-Path $PSScriptRoot 'Habitat.Process.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Habitat.Storage.psm1') -Force
$null = [IO.Directory]::CreateDirectory($verifyArtifacts)
$verificationOperation = [Habitat.Storage.GeneratedStorage]::BeginOperation($verifyArtifacts, @($verifyOutput))
$windowsPowerShell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'

function Invoke-Phase([string]$Name, [string]$Program, [string[]]$Arguments, [int]$Limit = $SuiteTimeoutSeconds) {
    if ($verifyWatch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw "Verification budget exhausted before $Name." }
    $process = Start-HabitatProcess -Name $Name -FilePath $Program -Arguments $Arguments -LogDirectory $verifyLogs -TimeoutSeconds $Limit
    Wait-HabitatProcesses -Checks @($process) -Budget $verifyWatch -BudgetSeconds $TimeoutSeconds
}
function Invoke-ScriptPhase([string]$Name, [string]$Path, [string[]]$Parameters = @(), [int]$Limit = $SuiteTimeoutSeconds) {
    $commandLine = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $verifyRoot $Path)) + $Parameters
    Invoke-Phase $Name $windowsPowerShell $commandLine $Limit
}
function Invoke-DocumentationChecks {
    Invoke-Phase 'docs-regressions' 'node' @('tests/docs.checks.cjs', (Join-Path $verifyOutput 'docs-regression'))
    Invoke-Phase 'docs-links' 'node' @('scripts/check-docs.cjs')
}
function Invoke-ExtensionChecks {
    $scripts = @(Get-ChildItem -LiteralPath (Join-Path $verifyRoot 'extension') -Filter '*.js' | ForEach-Object FullName)
    $scripts += @('vscode-extension/extension.js', 'vscode-extension/terminal-runs.js')
    foreach ($script in $scripts) {
        Invoke-Phase ('syntax-' + [IO.Path]::GetFileNameWithoutExtension($script)) 'node' @('--check', $script)
    }
    Invoke-Phase 'extension-manifest' 'node' @('-e', "JSON.parse(require('fs').readFileSync('extension/manifest.json', 'utf8'))")
    foreach ($suite in 'extension-background', 'extension-consent', 'extension-translation', 'vscode-extension', 'extension-search',
        'extension-youtube', 'localization', 'extension-facebook', 'extension-browser-contacts', 'extension-facebook-messaging') {
        Invoke-Phase $suite 'node' @("tests/$suite.checks.cjs")
    }
}
function Invoke-FullChecks {
    Invoke-DocumentationChecks
    Invoke-ScriptPhase 'process-budget' 'tests/process-budget.checks.ps1' @('-TestDirectory', (Join-Path $verifyOutput 'process-budget'))
    Invoke-ScriptPhase 'release-retirement' 'tests/release-retirement.checks.ps1' @('-TestDirectory', (Join-Path $verifyOutput 'release-retirement'))
    Invoke-ScriptPhase 'build-regression' 'tests/build.checks.ps1' @('-TestDirectory', (Join-Path $verifyOutput 'build-regression'))
    Invoke-ExtensionChecks

    $checksSolution = 'tests/Habitat.Checks.slnf'
    $solution = if ($SkipBuild) { $checksSolution } else { 'Habitat.sln' }
    $buildArgs = @('build', $solution, '--configuration', 'Release', '-p:Platform=x64', '-p:HabitatUiChecks=false',
        "-p:BaseOutputPath=$verifyOutput/build/")
    if ($NoRestore) { $buildArgs += '--no-restore' }
    Invoke-Phase 'dotnet-build' 'dotnet' $buildArgs 210

    $releaseBin = Join-Path $verifyOutput 'build/Release'
    $projects = (Get-Content -LiteralPath $checksSolution -Raw | ConvertFrom-Json).solution.projects
    $checkProcesses = @()
    try {
        foreach ($project in $projects) {
            $name = [IO.Path]::GetFileNameWithoutExtension($project)
            if (-not $name.EndsWith('.Checks')) { continue }
            $executable = @(Get-ChildItem -LiteralPath $releaseBin -Filter "$name.exe" -File -Recurse)
            if ($executable.Count -ne 1) { throw "Expected one compiled executable for $name." }
            $checkProcesses += Start-HabitatProcess -Name $name -FilePath $executable[0].FullName -LogDirectory $verifyLogs -TimeoutSeconds $SuiteTimeoutSeconds
        }
    } catch {
        foreach ($check in $checkProcesses) { Close-HabitatProcess $check }
        throw
    }
    Wait-HabitatProcesses -Checks $checkProcesses -Budget $verifyWatch -BudgetSeconds $TimeoutSeconds

    $env:HABITAT_RELEASE_VERIFIER = Join-Path $releaseBin 'net10.0/Habitat.Release.Verify.dll'
    Invoke-ScriptPhase 'payload' 'tests/payload.checks.ps1' @('-ChecksAssembly', (Join-Path $releaseBin 'net10.0/Habitat.Core.Checks.dll'),
        '-TestDirectory', (Join-Path $verifyOutput 'payload'))
    foreach ($suite in 'release', 'storage', 'update', 'packaging', 'signing-preparation') {
        Invoke-ScriptPhase $suite "tests/$suite.checks.ps1"
    }
    $chromeOutput = Join-Path $verifyOutput 'chrome'
    Invoke-ScriptPhase 'package-chrome' 'scripts/package-chrome-extension.ps1' @('-OutputDirectory', $chromeOutput)
    $chromeZip = Get-ChildItem -LiteralPath $chromeOutput -Filter '*.zip' | Select-Object -First 1
    Invoke-ScriptPhase 'chrome-package' 'tests/chrome-package.checks.ps1' @('-PackagePath', $chromeZip.FullName)
    Invoke-ScriptPhase 'package' 'tests/package.checks.ps1'
    if (-not $SkipUi) {
        Invoke-ScriptPhase 'native-ui' 'scripts/check-tasks-ui.ps1' @('-OutputDirectory', (Join-Path $verifyOutput 'ui'), '-TimeoutSeconds', '180') 240
    }
}

Push-Location $verifyRoot
try {
    switch ($CheckProfile) {
        'Docs' { Invoke-DocumentationChecks }
        'Extensions' { Invoke-DocumentationChecks; Invoke-ExtensionChecks }
        'Full' { Invoke-FullChecks }
    }
    Invoke-Phase 'whitespace' 'git' @('diff', '--check')
    Complete-HabitatGeneratedOutput $verifyArtifacts $verifyOutput 'test'
    Write-Host ('{0} verification completed in {1:N1}s.' -f $CheckProfile, $verifyWatch.Elapsed.TotalSeconds)
} finally {
    $env:HABITAT_RELEASE_VERIFIER = $previousVerifier
    $verificationOperation.Dispose()
    Pop-Location
}
try { $null = Invoke-HabitatStorageMaintenance -ArtifactsRoot $verifyArtifacts -Clean -Automatic }
catch { Write-Warning 'Generated storage maintenance could not finish. Verification output was retained.' }
