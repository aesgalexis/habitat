param([switch]$SkipBuild, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$verifyRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $verifyRoot '.dotnet-local'
$env:NUGET_PACKAGES = Join-Path $verifyRoot '.nuget\packages'
$verifyOutput = Join-Path $verifyRoot 'artifacts\verification'
Push-Location $verifyRoot
try {
    node tests/docs.checks.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Documentation checker regression cases failed.' }
    node scripts/check-docs.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Documentation links failed.' }
    if (-not $NoRestore) {
        dotnet restore Habitat.sln -p:Platform=x64
        if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    }
    foreach ($script in Get-ChildItem -LiteralPath (Join-Path $verifyRoot 'extension') -Filter '*.js') {
        node --check $script.FullName
        if ($LASTEXITCODE -ne 0) { throw "JavaScript syntax check failed: $($script.Name)" }
    }
    node -e "JSON.parse(require('fs').readFileSync('extension/manifest.json', 'utf8'))"
    if ($LASTEXITCODE -ne 0) { throw 'Invalid extension manifest.' }
    node tests/extension-background.checks.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Extension checks failed.' }
    node tests/extension-consent.checks.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Extension consent checks failed.' }
    node tests/extension-translation.checks.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Translation extension checks failed.' }
    node --check vscode-extension/extension.js
    if ($LASTEXITCODE -ne 0) { throw 'VS Code extension syntax check failed.' }
    node --check vscode-extension/terminal-runs.js
    if ($LASTEXITCODE -ne 0) { throw 'VS Code terminal tracking syntax check failed.' }
    node tests/vscode-extension.checks.cjs
    if ($LASTEXITCODE -ne 0) { throw 'VS Code extension checks failed.' }
    node tests/extension-search.checks.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Search extension checks failed.' }
    node tests/extension-youtube.checks.cjs
    if ($LASTEXITCODE -ne 0) { throw 'YouTube extension checks failed.' }
    node tests/localization.checks.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Interface localization checks failed.' }
    node tests/extension-facebook.checks.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Facebook extension checks failed.' }
    node tests/extension-browser-contacts.checks.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Automatic browser contact checks failed.' }
    node tests/extension-facebook-messaging.checks.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Facebook messaging checks failed.' }
    foreach ($suite in 'Appearance', 'Bridge', 'Core', 'Destination', 'Gmail', 'Contacts', 'Tasks', 'Weather', 'Hotkey', 'Window') {
        dotnet run --project "tests/Habitat.$suite.Checks" --configuration Release --no-restore `
            "-p:BaseOutputPath=$verifyOutput/$suite/"
        if ($LASTEXITCODE -ne 0) { throw "$suite checks failed." }
    }
    & (Join-Path $verifyRoot 'tests\release.checks.ps1')
    & (Join-Path $verifyRoot 'tests\update.checks.ps1')
    & (Join-Path $verifyRoot 'tests\packaging.checks.ps1')
    & (Join-Path $verifyRoot 'tests\signing-preparation.checks.ps1')
    $chromeOutput = Join-Path $verifyOutput ('chrome-' + [guid]::NewGuid().ToString('N'))
    & (Join-Path $verifyRoot 'scripts\package-chrome-extension.ps1') -OutputDirectory $chromeOutput
    $chromeZip = Get-ChildItem -LiteralPath $chromeOutput -Filter '*.zip' | Select-Object -First 1
    & (Join-Path $verifyRoot 'tests\chrome-package.checks.ps1') -PackagePath $chromeZip.FullName
    & (Join-Path $verifyRoot 'tests\package.checks.ps1')
    if (-not $SkipBuild) {
        dotnet build Habitat.sln --configuration Release --no-restore -p:Platform=x64 -p:HabitatUiChecks=false `
            "-p:BaseOutputPath=$verifyOutput/build/"
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }
    git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace checks failed.' }
    Write-Host 'All HABITAT checks passed. Production binaries and user data were not used.'
} finally { Pop-Location }
