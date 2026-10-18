param(
    [string]$Version,
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][uri]$InstallerUrl,
    [uri]$PackageUrl,
    [string]$OutputDirectory,
    [switch]$VerifyDownload
)
$ErrorActionPreference = 'Stop'
$manifestRepo = Split-Path -Parent $PSScriptRoot
if (-not $Version) { $Version = (Get-Content -LiteralPath (Join-Path $manifestRepo 'VERSION') -Raw).Trim() }
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Version must be numeric SemVer.' }
$metadata = Get-Content -LiteralPath (Join-Path $manifestRepo 'packaging/metadata.json') -Raw | ConvertFrom-Json
foreach ($part in $Version.Split('.')) {
    if ([long]$part -gt 65535) { throw 'Version components must be between 0 and 65535.' }
}
if (-not $PackageUrl) { $PackageUrl = [uri]$metadata.PackageUrl }
foreach ($address in $InstallerUrl, $PackageUrl) {
    if (-not $address.IsAbsoluteUri -or $address.Scheme -ne 'https' -or $address.UserInfo -or $address.Fragment) {
        throw 'Package and installer URLs must use absolute HTTPS URLs without credentials or fragments.'
    }
}
$installerName = "HABITAT-$Version-x64-setup.exe"
if ([IO.Path]::GetFileName($InstallerPath) -cne $installerName -or
    [uri]::UnescapeDataString(($InstallerUrl.AbsolutePath.Split('/') | Select-Object -Last 1)) -cne $installerName -or
    $InstallerUrl.AbsolutePath -match '/latest/') {
    throw "Use a versioned URL and local installer named $installerName."
}
$installerFile = Get-Item -LiteralPath $InstallerPath
if ($installerFile.PSIsContainer -or $installerFile.Length -eq 0) { throw 'Installer must be a non-empty file.' }
$hash = (Get-FileHash -LiteralPath $installerFile.FullName -Algorithm SHA256).Hash
if ($VerifyDownload) {
    $downloadRoot = Join-Path $manifestRepo ('artifacts/winget-download-check/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
    $download = Join-Path $downloadRoot $installerName
    # No GitHub token or authentication: this must work for the public client.
    Invoke-WebRequest -Uri $InstallerUrl.AbsoluteUri -OutFile $download -UseBasicParsing
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $hash) {
        throw 'The public download does not match the local installer. No manifests were written.'
    }
}
$identifier = $metadata.PackageIdentifier
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $manifestRepo 'artifacts/winget' }
$parts = $identifier.Split('.')
$manifestDirectory = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ("manifests/{0}/{1}/{2}/$Version" -f $identifier.Substring(0, 1).ToLowerInvariant(), $parts[0], $parts[1])
if (Test-Path -LiteralPath $manifestDirectory) { throw "Manifest directory already exists: $manifestDirectory. Use a fresh output directory." }
New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
function Quote-Yaml([string]$Value) { return "'" + $Value.Replace("'", "''") + "'" }
function Write-Manifest([string]$Name, [string]$Body) {
    [IO.File]::WriteAllText((Join-Path $manifestDirectory $Name), ($Body.Trim() + "`n"), [Text.UTF8Encoding]::new($false))
}
$schemaVersion = '1.12.0'
$header = "PackageIdentifier: $identifier`nPackageVersion: '$Version'"
Write-Manifest "$identifier.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schemaVersion.schema.json
$header
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schemaVersion
"@
Write-Manifest "$identifier.locale.en-US.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schemaVersion.schema.json
$header
PackageLocale: en-US
Publisher: $(Quote-Yaml $metadata.Publisher)
PackageName: $(Quote-Yaml $metadata.PackageName)
PackageUrl: $(Quote-Yaml $PackageUrl.AbsoluteUri.TrimEnd('/'))
License: $($metadata.License)
ShortDescription: $(Quote-Yaml $metadata.ShortDescription)
Moniker: habitat
Tags:
- productivity
- desktop
- tasks
ManifestType: defaultLocale
ManifestVersion: $schemaVersion
"@
Write-Manifest "$identifier.installer.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schemaVersion.schema.json
$header
InstallerLocale: en-US
InstallerType: inno
Scope: user
Platform:
- Windows.Desktop
MinimumOSVersion: $($metadata.MinimumOSVersion)
InstallModes:
- interactive
- silent
- silentWithProgress
InstallerSwitches:
  Silent: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
  SilentWithProgress: /SILENT /SUPPRESSMSGBOXES /NORESTART /SP-
  Log: /LOG="<LOGPATH>"
UpgradeBehavior: install
ReleaseDate: $(Get-Date -Format 'yyyy-MM-dd')
AppsAndFeaturesEntries:
- DisplayName: $(Quote-Yaml $metadata.PackageName)
  Publisher: $(Quote-Yaml $metadata.Publisher)
  DisplayVersion: '$Version'
  ProductCode: '{$($metadata.AppId)}_is1'
  InstallerType: inno
Installers:
- Architecture: x64
  InstallerUrl: $(Quote-Yaml $InstallerUrl.AbsoluteUri)
  InstallerSha256: $hash
ManifestType: installer
ManifestVersion: $schemaVersion
"@
Write-Host "WinGet manifests: $manifestDirectory"
Write-Host "Validate with: winget validate --manifest `"$manifestDirectory`""
if (-not $VerifyDownload) { Write-Host 'Candidate only: run again with -VerifyDownload after publishing the final installer.' }
