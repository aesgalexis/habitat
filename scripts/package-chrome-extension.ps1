param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$extension = Join-Path $repo 'extension'
$manifest = Get-Content -LiteralPath (Join-Path $extension 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.manifest_version -ne 3 -or $manifest.description.Length -gt 132 -or
    $manifest.version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Invalid store manifest.' }
Import-Module (Join-Path $PSScriptRoot 'Habitat.Extension.psm1') -Force
$files = @(Get-HabitatChromeExtensionFiles)
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $extension $file) -PathType Leaf)) { throw "Missing store resource: $file" }
}
$artifacts = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $artifacts ('chrome-store/' + $manifest.version) }
$output = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repo $OutputDirectory }))
if (-not $output.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Chrome package output must stay under artifacts.'
}
$zip = Join-Path $output "HABITAT-Browser-Bridge-$($manifest.version).zip"
if (Test-Path -LiteralPath $zip) { throw 'Package already exists. Use a new output directory.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $extension $file), $file,
            [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $repo 'LICENSE'), 'LICENSE.txt') | Out-Null
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
[IO.File]::WriteAllText(($zip + '.sha256'), "$hash  $([IO.Path]::GetFileName($zip))`n")
foreach ($asset in 'promo-440x280.png', 'google-start-1280x800.png', 'google-results-1280x800.png', 'listing.json') {
    Copy-Item -LiteralPath (Join-Path $repo ('packaging/chrome-store/' + $asset)) -Destination $output
}
Write-Host "Chrome Web Store package: $zip"
Write-Host "SHA256: $hash"
