param(
    [string]$Version,
    [string]$InnoCompilerPath,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$installerRepo = Split-Path -Parent $PSScriptRoot
$changes = git -C $installerRepo status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Build release installers from a clean committed working tree.' }
$commit = git -C $installerRepo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not determine the source commit.' }
$sourceVersion = (Get-Content -LiteralPath (Join-Path $installerRepo 'VERSION') -Raw).Trim()
if (-not $Version) { $Version = $sourceVersion }
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or $Version -ne $sourceVersion) {
    throw 'Installer version must be numeric SemVer and match VERSION. Update VERSION before building a new release.'
}
$metadata = Get-Content -LiteralPath (Join-Path $installerRepo 'packaging/metadata.json') -Raw | ConvertFrom-Json
foreach ($part in $Version.Split('.')) {
    if ([long]$part -gt 65535) { throw 'Version components must be between 0 and 65535.' }
}
if (-not $InnoCompilerPath) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compilerCommand) { $InnoCompilerPath = $compilerCommand.Source }
    else { $InnoCompilerPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
}
if (-not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
    throw 'Install Inno Setup 6.7.1 or pass -InnoCompilerPath pointing to ISCC.exe.'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $installerRepo "artifacts/distribution/$Version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$installer = Join-Path $OutputDirectory "HABITAT-$Version-x64-setup.exe"
if (Test-Path -LiteralPath $installer) { throw "Installer already exists: $installer. Use a fresh output directory." }
$work = Join-Path $installerRepo ('artifacts/packaging/' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
New-Item -ItemType Directory -Path $payload, $OutputDirectory -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $installerRepo '.dotnet-local'
$env:NUGET_PACKAGES = Join-Path $installerRepo '.nuget/packages'
dotnet publish (Join-Path $installerRepo 'src/Habitat.App/Habitat.App.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:Platform=x64 -p:HabitatUiChecks=false "-p:Version=$Version" "-p:FileVersion=$Version.0" `
    --output $payload
if ($LASTEXITCODE -ne 0) { throw 'HABITAT publish failed.' }
# PublishHotkeyCompanion runs in the app's Publish target. Never package a build
# without the companion or either self-contained runtime.
foreach ($file in 'HABITAT.exe', 'HABITAT.dll', 'HABITAT.Hotkey.exe', 'HABITAT.Hotkey.dll',
    'HABITAT.runtimeconfig.json', 'HABITAT.Hotkey.runtimeconfig.json', 'coreclr.dll',
    'Microsoft.AspNetCore.dll', 'Microsoft.UI.Xaml.dll', 'Assets/HabitatDot.ico') {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $file) -PathType Leaf)) { throw "Payload is missing $file." }
}
Copy-Item -LiteralPath (Join-Path $installerRepo 'LICENSE') -Destination (Join-Path $payload 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $installerRepo 'packaging/GETTING_STARTED.txt') -Destination $payload
$browserExtension = Join-Path $payload 'browser-extension'
New-Item -ItemType Directory -Path $browserExtension -Force | Out-Null
Import-Module (Join-Path $PSScriptRoot 'Habitat.Extension.psm1') -Force
Copy-HabitatChromeExtension -Source (Join-Path $installerRepo 'extension') -Destination $browserExtension
Copy-Item -LiteralPath (Join-Path $installerRepo 'docs/GETTING_STARTED.md') -Destination $payload
& (Join-Path $PSScriptRoot 'build-vscode-extension.ps1') -OutputDirectory (Join-Path $payload 'vscode-extension')
$bridge = Get-Content -LiteralPath (Join-Path $browserExtension 'manifest.json') -Raw | ConvertFrom-Json
$vscode = Get-Content -LiteralPath (Join-Path $installerRepo 'vscode-extension/package.json') -Raw | ConvertFrom-Json
$release = [ordered]@{ version = $Version; runtime = 'win-x64'; sourceCommit = $commit.Trim(); browserExtensionVersion = $bridge.version; vscodeExtensionVersion = $vscode.version }
[IO.File]::WriteAllText((Join-Path $payload 'release.json'), ($release | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Import-Module (Join-Path $PSScriptRoot 'Habitat.Release.psm1') -Force
New-HabitatBuildManifest $payload
Test-HabitatBuildManifest $payload
$changes = git -C $installerRepo status --porcelain
$finalCommit = git -C $installerRepo rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $changes -or $finalCommit -ne $commit) { throw 'Source files changed while building the installer.' }
& $InnoCompilerPath '/Qp' "/DHabitatVersion=$Version" "/DHabitatPublisher=$($metadata.Publisher)" `
    "/DHabitatAppId=$($metadata.AppId)" "/DHabitatPackageUrl=$($metadata.PackageUrl)" `
    "/DPayloadDirectory=$payload" "/DInstallerOutputDirectory=$OutputDirectory" `
    (Join-Path $installerRepo 'packaging/HABITAT.iss')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installer)) { throw 'Installer compilation failed.' }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
[IO.File]::WriteAllText(($installer + '.sha256'), "$hash  $([IO.Path]::GetFileName($installer))`n", [Text.UTF8Encoding]::new($false))
Write-Host "Installer: $installer"
Write-Host "SHA256: $hash"
Write-Host "Verified payload: $payload"
