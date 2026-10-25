param(
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [string]$InstallerPath,
    [string]$ReleaseMetadataPath,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$preparationRepo = Split-Path -Parent $PSScriptRoot
$preparationArtifacts = [IO.Path]::GetFullPath((Join-Path $preparationRepo 'artifacts'))
$config = Get-Content -LiteralPath (Join-Path $preparationRepo 'packaging/signpath/preparation.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($config.schemaVersion -ne 1 -or $config.status -ne 'deferred-by-owner' -or $config.submissionAllowed -ne $false -or $config.signingEnabled -ne $false) {
    throw 'This offline tool requires the owner-deferred, inactive signing configuration.'
}
if (@($config.ownBinaries).Count -eq 0 -or @($config.ownBinaries | Select-Object -Unique).Count -ne @($config.ownBinaries).Count) { throw 'Invalid own-binary allowlist.' }
foreach ($name in $config.ownBinaries) {
    if ($name -cne [IO.Path]::GetFileName($name) -or $name -notmatch '^Habitat[.A-Za-z0-9]*\.(exe|dll)$') { throw 'Own binaries must be explicit root filenames.' }
}
function Resolve-PreparationPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $preparationRepo $Path }))
    if (-not $full.StartsWith($preparationArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Signing preparation inputs and outputs must stay inside project artifacts; user data is not an input.'
    }
    for ($walk = $full; $walk; $walk = Split-Path -Parent $walk) {
        if ((Test-Path -LiteralPath $walk) -and ((Get-Item -LiteralPath $walk -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Signing preparation paths cannot contain filesystem links.'
        }
    }
    return $full
}
$payload = Resolve-PreparationPath $PayloadDirectory
if (-not (Test-Path -LiteralPath $payload -PathType Container)) { throw 'Payload directory not found.' }
Import-Module (Join-Path $PSScriptRoot 'Habitat.Release.psm1') -Force
Test-HabitatBuildManifest $payload
$manifest = Get-Content -LiteralPath (Join-Path $payload 'build-manifest.json') -Raw | ConvertFrom-Json
$version = (Get-Content -LiteralPath (Join-Path $preparationRepo 'VERSION') -Raw).Trim()
$expectedFileVersion = "$version.0"
$pending = @('owner-deferred', 'application-not-submitted', 'provider-not-configured') + @($config.packagingPrerequisites)
function Inspect-OwnBinary([string]$Path, [string]$Name) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return [pscustomobject]@{ path = $Name; present = $false; metadataMatches = $false } }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    [pscustomobject]@{
        path = $Name; present = $true; length = (Get-Item -LiteralPath $Path).Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
        product = $info.ProductName; company = $info.CompanyName; fileVersion = $info.FileVersion
        metadataMatches = $info.ProductName -ceq $config.product -and $info.CompanyName -ceq $config.company -and $info.FileVersion -ceq $expectedFileVersion
    }
}
$own = @($config.ownBinaries | ForEach-Object { Inspect-OwnBinary (Join-Path $payload $_) $_ })
foreach ($entry in $own) {
    if (-not $entry.present) { $pending += "missing-own-binary:$($entry.path)" }
    elseif (-not $entry.metadataMatches) { $pending += "metadata-mismatch:$($entry.path)" }
}
$unknownOwn = @($manifest.files | Where-Object { $_.path -match '(^|/)Habitat[^/]*\.(exe|dll)$' -and $_.path -notin $config.ownBinaries })
foreach ($entry in $unknownOwn) { $pending += "unclassified-own-binary:$($entry.path)" }
$excluded = @($manifest.files | Where-Object { $_.path -match '\.(dll|exe)$' -and $_.path -notin $config.ownBinaries } |
    Select-Object path, length, sha256)
$installer = $null
if ($InstallerPath) {
    $installerFull = Resolve-PreparationPath $InstallerPath
    if ([IO.Path]::GetFileName($installerFull) -cne "HABITAT-$version-x64-setup.exe") { throw 'Installer filename differs from VERSION.' }
    $installer = Inspect-OwnBinary $installerFull ([IO.Path]::GetFileName($installerFull))
    if (-not $installer.present -or -not $installer.metadataMatches) { $pending += 'installer-missing-or-metadata-mismatch' }
} else { $pending += 'installer-not-inspected' }
$release = $null
if ($ReleaseMetadataPath) {
    $releasePath = Resolve-PreparationPath $ReleaseMetadataPath
    $metadata = Get-Content -LiteralPath $releasePath -Raw | ConvertFrom-Json
    if ($metadata.version -ne $version -or $metadata.runtime -ne 'win-x64' -or $metadata.sourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'Invalid release provenance metadata.' }
    # Never copy arbitrary fields from an input JSON into a shareable dossier.
    $release = [pscustomobject]@{ version = $metadata.version; runtime = $metadata.runtime; sourceCommit = $metadata.sourceCommit
        browserExtensionVersion = $metadata.browserExtensionVersion; vscodeExtensionVersion = $metadata.vscodeExtensionVersion }
} else { $pending += 'release-provenance-not-provided' }
$commit = git -C $preparationRepo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not read local source commit.' }
$changes = git -C $preparationRepo status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect local working tree.' }
if ($changes) { $pending += 'working-tree-has-unreleased-changes' }
if ($release -and $release.sourceCommit -ne $commit) { $pending += 'payload-source-differs-from-current-head' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $preparationArtifacts ('signpath-preparation/' + [guid]::NewGuid().ToString('N')) }
$output = Resolve-PreparationPath $OutputDirectory
if ($output -eq $payload -or $output.StartsWith($payload + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $payload.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Report output must be separate from the payload.' }
if (Test-Path -LiteralPath $output) { throw 'Use a new report output directory.' }
$report = [ordered]@{
    schemaVersion = 1; provider = $config.provider; status = $config.status; readyToApply = $false
    submissionAllowed = $false; signingEnabled = $false; networkRequestsMade = $false
    sourceCommit = $commit.Trim(); workingTreeClean = -not [bool]$changes; version = $version
    expectedMetadata = @{ product = $config.product; company = $config.company; fileVersion = $expectedFileVersion }
    payloadManifestVerified = $true; payloadFileCount = @($manifest.files).Count
    ownBinaries = $own; installer = $installer; releaseMetadata = $release
    excludedFromHabitatSigning = $excluded; pending = @($pending | Sort-Object -Unique)
    signatureValidation = 'Not performed. Offline inspection does not validate Authenticode trust or timestamps.'
    notice = 'Local preparation only. Do not upload, submit or sign these artifacts without the owner resuming the process.'
}
New-Item -ItemType Directory -Path $output | Out-Null
[IO.File]::WriteAllText((Join-Path $output 'readiness.json'), ($report | ConvertTo-Json -Depth 9), [Text.UTF8Encoding]::new($false))
$lines = @('# HABITAT - SignPath preparation', '', 'Status: deferred by owner. Application and signing remain disabled.', '',
    "Version: $version", "Verified payload files: $(@($manifest.files).Count)", "Own binaries inspected: $($own.Count)",
    "Excluded PE files: $($excluded.Count)", '', '## Pending before applying', '') + @($report.pending | ForEach-Object { "- $_" })
[IO.File]::WriteAllText((Join-Path $output 'readiness.md'), ($lines -join "`n"), [Text.UTF8Encoding]::new($false))
Write-Host "Offline SignPath preparation report: $output"
Write-Host 'Signing and application are disabled. No artifact was modified, executed or submitted.'
