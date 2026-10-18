param([string]$PackagePath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $repo 'VERSION') -Raw).Trim()
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Invalid desktop version.' }
if ((Get-Content -LiteralPath (Join-Path $repo 'CHANGELOG.md') -Raw -Encoding UTF8) -notmatch "(?m)^## $([regex]::Escape($version)) \u2014 \d{4}-\d{2}-\d{2}\r?$") {
    throw 'Current version is missing from CHANGELOG.md.'
}
foreach ($file in @((Join-Path $repo 'Install-HABITAT.ps1')) + @(Get-ChildItem -LiteralPath (Join-Path $repo 'scripts') -Filter '*.ps1' | Select-Object -ExpandProperty FullName)) {
    $tokens = $null; $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors) { throw "PowerShell syntax errors in ${file}: $errors" }
}
$checks = Join-Path $repo ('artifacts\package-checks\' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $checks 'package'
$payload = Join-Path $package 'artifacts\staged\app'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Import-Module (Join-Path $repo 'scripts\Habitat.Release.psm1') -Force
if ($PackagePath) {
    $zip = (Resolve-Path -LiteralPath $PackagePath).Path
    $hashLine = (Get-Content -LiteralPath ($zip + '.sha256') -Raw).Trim()
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hashLine -ne ($hash + ' *' + [IO.Path]::GetFileName($zip))) { throw 'Package SHA-256 does not match.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $package)
    $metadata = Get-Content -LiteralPath (Join-Path $package 'release.json') -Raw | ConvertFrom-Json
    $commit = git -C $repo rev-parse HEAD
    if ($metadata.version -ne $version -or $metadata.runtime -ne 'win-x64' -or $metadata.sourceCommit -ne $commit) {
        throw 'Package version, runtime or source commit does not match.'
    }
    $bridge = Get-Content -LiteralPath (Join-Path $package 'extension\manifest.json') -Raw | ConvertFrom-Json
    if ($bridge.version -ne $metadata.browserExtensionVersion) { throw 'Packaged browser version does not match.' }
    $vsix = Join-Path $package "vscode-extension\habitat-terminal-$($metadata.vscodeExtensionVersion).vsix"
    $archive = [IO.Compression.ZipFile]::OpenRead($vsix)
    try {
        $reader = [IO.StreamReader]::new($archive.GetEntry('extension/package.json').Open())
        try { $vscode = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($vscode.version -ne $metadata.vscodeExtensionVersion) { throw 'Packaged VSIX version does not match.' }
    } finally { $archive.Dispose() }
    foreach ($assembly in 'HABITAT.dll', 'HABITAT.Hotkey.dll', 'Habitat.Core.dll', 'Habitat.Activation.dll') {
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload $assembly))
        if ($info.FileVersion -ne "$version.0") { throw "Incorrect assembly version: $assembly" }
    }
    foreach ($name in 'HABITAT', 'HABITAT.Hotkey') {
        $runtime = Get-Content -LiteralPath (Join-Path $payload "$name.runtimeconfig.json") -Raw | ConvertFrom-Json
        if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks -or
            -not $runtime.runtimeOptions.includedFrameworks) { throw "$name requires an external .NET runtime." }
    }
    foreach ($file in 'coreclr.dll', 'Microsoft.AspNetCore.dll', 'Microsoft.UI.Xaml.dll') {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $file))) { throw "Package is missing runtime file $file." }
    }
} else {
    foreach ($name in 'HABITAT.exe', 'HABITAT.dll', 'Habitat.Core.dll', 'HABITAT.Hotkey.exe', 'HABITAT.Hotkey.dll', 'Habitat.Activation.dll', 'resource.txt') {
        [IO.File]::WriteAllText((Join-Path $payload $name), "synthetic-$name")
    }
    New-HabitatBuildManifest $payload
    foreach ($name in 'Install-HABITAT.ps1', 'HABITAT.vbs', 'HABITAT-Hotkey.vbs', 'VERSION', 'README.md', 'CHANGELOG.md', 'LICENSE') {
        Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $package
    }
    foreach ($name in 'scripts', 'docs', 'extension', 'vscode-extension') {
        New-Item -ItemType Directory -Path (Join-Path $package $name) -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $repo 'scripts\Habitat.Release.psm1') -Destination (Join-Path $package 'scripts')
    [IO.File]::WriteAllText((Join-Path $package 'release.json'), (@{ version = $version; runtime = 'win-x64' } | ConvertTo-Json))
}
Test-HabitatBuildManifest $payload
$destination = Join-Path $checks 'installed with spaces'
$installer = Join-Path $package 'Install-HABITAT.ps1'
& $installer -InstallDirectory $destination -NoLaunch -NoShortcuts
$active = Get-HabitatActiveDirectory (Join-Path $destination 'artifacts')
Test-HabitatBuildManifest $active
if (-not (Test-Path -LiteralPath (Join-Path $destination 'HABITAT-Hotkey.vbs'))) { throw 'Installed shortcut launcher is missing.' }
# An update must preserve the previous release, including files with open handles.
$held = [IO.File]::OpenRead((Join-Path $active 'HABITAT.exe'))
try { & $installer -InstallDirectory $destination -NoLaunch -NoShortcuts } finally { $held.Dispose() }
$updated = Get-HabitatActiveDirectory (Join-Path $destination 'artifacts')
if ($updated -eq $active -or -not (Test-Path -LiteralPath $active)) { throw 'Update overwrote the previous release.' }
Test-HabitatBuildManifest $updated
# The ZIP layout must never overwrite the separate Inno Setup installation.
$exeInstallation = Join-Path $checks 'exe installation'
New-Item -ItemType Directory -Path $exeInstallation -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $exeInstallation 'HABITAT.exe'), 'existing-exe-installation')
$rejected = $false
try { & $installer -InstallDirectory $exeInstallation -NoLaunch -NoShortcuts }
catch { $rejected = $_.Exception.Message -like '*EXE installation*' }
if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $exeInstallation 'artifacts'))) {
    throw 'The ZIP installer modified an EXE installation.'
}
# Corruption must be rejected before changing the active version.
[IO.File]::AppendAllText((Join-Path $payload 'HABITAT.dll'), 'corruption')
$rejected = $false
try { & $installer -InstallDirectory $destination -NoLaunch -NoShortcuts } catch { $rejected = $true }
if (-not $rejected -or (Get-HabitatActiveDirectory (Join-Path $destination 'artifacts')) -ne $updated) {
    throw 'A corrupt package changed the installed release.'
}
$rejected = $false
try { & $installer -InstallDirectory $package -NoLaunch -NoShortcuts } catch { $rejected = $true }
if (-not $rejected) { throw 'Overlapping installation was accepted.' }
Write-Host 'Package checks passed: versions, script syntax, installation with spaces, update with locked files, previous release retention, corruption and path boundaries. No app, shortcuts or user data were used.'
