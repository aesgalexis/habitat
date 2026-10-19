param([string]$StageDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$changes = git -C $repo status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Package releases from a clean committed working tree.' }
$commit = git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not determine the source commit.' }
$version = (Get-Content -LiteralPath (Join-Path $repo 'VERSION') -Raw).Trim()
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'VERSION must contain a numeric SemVer version.' }
$artifactsRoot = Join-Path $repo 'artifacts'
if (-not $StageDirectory) {
    $StageDirectory = Join-Path $artifactsRoot ('staged\package-' + [guid]::NewGuid().ToString('N'))
    & (Join-Path $PSScriptRoot 'build-winui.ps1') -OutputDirectory $StageDirectory
}
$stage = (Resolve-Path -LiteralPath $StageDirectory).Path
if (-not $stage.StartsWith($artifactsRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Stage must stay inside artifacts.' }
Import-Module (Join-Path $PSScriptRoot 'Habitat.Release.psm1') -Force
Test-HabitatBuildManifest $stage
foreach ($assembly in 'HABITAT.dll', 'HABITAT.Hotkey.dll', 'Habitat.Core.dll', 'Habitat.Activation.dll') {
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stage $assembly))
    if ($info.FileVersion -ne "$version.0") { throw "$assembly version differs from VERSION. Rebuild the release." }
}
& (Join-Path $PSScriptRoot 'build-vscode-extension.ps1')
$bridge = Get-Content -LiteralPath (Join-Path $repo 'extension\manifest.json') -Raw | ConvertFrom-Json
$vscode = Get-Content -LiteralPath (Join-Path $repo 'vscode-extension\package.json') -Raw | ConvertFrom-Json
$output = Join-Path $artifactsRoot 'packages'
$bundle = Join-Path $output ('bundle-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $bundle 'artifacts\staged\app'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Get-ChildItem -LiteralPath $stage -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $payload -Recurse -Force }
foreach ($name in 'HABITAT.vbs', 'HABITAT-Hotkey.vbs', 'Install-HABITAT.cmd', 'Install-HABITAT.ps1', 'README.md', 'CHANGELOG.md', 'LICENSE', 'VERSION', 'docs') {
    Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $bundle -Recurse -Force
}
Import-Module (Join-Path $PSScriptRoot 'Habitat.Extension.psm1') -Force
Copy-HabitatChromeExtension -Source (Join-Path $repo 'extension') -Destination (Join-Path $bundle 'extension')
Copy-Item -LiteralPath (Join-Path $repo 'packaging/GETTING_STARTED.txt') -Destination $bundle
New-Item -ItemType Directory -Path (Join-Path $bundle 'scripts'), (Join-Path $bundle 'vscode-extension') -Force | Out-Null
foreach ($name in 'Habitat.Release.psm1', 'apply-staged-update.ps1') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $bundle 'scripts')
}
$vsixName = "habitat-terminal-$($vscode.version).vsix"
Copy-Item -LiteralPath (Join-Path $artifactsRoot "vscode\$vsixName") -Destination (Join-Path $bundle 'vscode-extension')
$metadata = [ordered]@{
    version = $version; runtime = 'win-x64'; sourceCommit = $commit.Trim()
    browserExtensionVersion = $bridge.version; vscodeExtensionVersion = $vscode.version
}
[IO.File]::WriteAllText((Join-Path $bundle 'release.json'), ($metadata | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Test-HabitatBuildManifest $payload
$finalCommit = git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $finalCommit -ne $commit) { throw 'Source commit changed while preparing the package.' }
$changes = git -C $repo status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Source files changed while preparing the package.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $output "HABITAT-$version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { throw 'Package already exists. Use a new version or move the previous local package.' }
[IO.Compression.ZipFile]::CreateFromDirectory($bundle, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip + '.sha256'), ($hash + ' *' + [IO.Path]::GetFileName($zip) + "`n"), [Text.UTF8Encoding]::new($false))
Write-Host "Release package: $zip"
Write-Host "SHA-256: $hash"
