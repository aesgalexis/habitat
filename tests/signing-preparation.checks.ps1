$ErrorActionPreference = 'Stop'
$signingRepo = Split-Path -Parent $PSScriptRoot
$config = Get-Content -LiteralPath (Join-Path $signingRepo 'packaging/signpath/preparation.json') -Raw | ConvertFrom-Json
$expected = @('HABITAT.exe','HABITAT.dll','HABITAT.Hotkey.exe','HABITAT.Hotkey.dll','Habitat.Core.dll','Habitat.Activation.dll')
if ($config.status -ne 'deferred-by-owner' -or $config.submissionAllowed -ne $false -or $config.signingEnabled -ne $false -or
    $config.organizationId -or $config.projectSlug -or $config.signingPolicySlug -or
    @($config.ownBinaries).Count -ne $expected.Count -or (Compare-Object $expected @($config.ownBinaries))) {
    throw 'Preparation must remain deferred, unconfigured and restricted to the own-binary allowlist.'
}
[xml]$properties = Get-Content -LiteralPath (Join-Path $signingRepo 'Directory.Build.props') -Raw
$publisher = (Get-Content -LiteralPath (Join-Path $signingRepo 'packaging/metadata.json') -Raw | ConvertFrom-Json).Publisher
if ($properties.Project.PropertyGroup.Product -cne $config.product -or $properties.Project.PropertyGroup.Company -cne $config.company -or $config.company -cne $publisher) {
    throw 'Build, packaging and signing metadata must agree.'
}
[xml]$appXml = Get-Content -LiteralPath (Join-Path $signingRepo 'packaging/signpath/app-artifact.xml') -Raw
$ns = [Xml.XmlNamespaceManager]::new($appXml.NameTable)
$ns.AddNamespace('s', 'http://signpath.io/artifact-configuration/v1')
$set = $appXml.SelectSingleNode('/s:artifact-configuration/s:zip-file/s:pe-file-set', $ns)
$included = @($set.SelectNodes('s:include', $ns) | ForEach-Object { $_.GetAttribute('path') })
if ($included.Count -ne $expected.Count -or (Compare-Object $expected $included) -or
    $set.GetAttribute('product-name') -cne $config.product -or $set.GetAttribute('company-name') -cne $config.company -or
    $set.GetAttribute('file-version') -cne '${version}' -or $set.SelectNodes('s:for-each/s:authenticode-sign', $ns).Count -ne 1) {
    throw 'The application XML must restrict names, metadata and signing scope.'
}
[xml]$installerXml = Get-Content -LiteralPath (Join-Path $signingRepo 'packaging/signpath/installer-artifact.xml') -Raw
$installerNs = [Xml.XmlNamespaceManager]::new($installerXml.NameTable)
$installerNs.AddNamespace('s', 'http://signpath.io/artifact-configuration/v1')
$pe = $installerXml.SelectSingleNode('/s:artifact-configuration/s:zip-file/s:pe-file', $installerNs)
if ($pe.GetAttribute('path') -cne $config.installerPattern -or $pe.GetAttribute('max-matches') -ne '1' -or
    $pe.GetAttribute('file-version') -cne '${version}' -or $pe.GetAttribute('product-name') -cne $config.product -or
    $pe.GetAttribute('company-name') -cne $config.company) { throw 'Installer XML must allow only one matching HABITAT installer.' }
foreach ($workflow in Get-ChildItem -LiteralPath (Join-Path $signingRepo '.github/workflows') -File) {
    $workflowCode = (Get-Content -LiteralPath $workflow.FullName | Where-Object { $_ -notmatch '^\s*#' }) -join "`n"
    if ($workflowCode -match '(?im)^\s*(?:-\s*)?uses:\s*signpath/|Submit-SigningRequest|SIGNPATH_API_TOKEN|https://app\.signpath\.io/Api') {
        throw 'A signing submission integration was added before the owner resumed it.'
    }
}
$checkRoot = Join-Path $signingRepo ('artifacts/signing-preparation-checks/' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $checkRoot 'payload with spaces'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
foreach ($name in $expected + @('Microsoft.ThirdParty.dll','Habitat.Unreviewed.dll')) {
    [IO.File]::WriteAllText((Join-Path $payload $name), 'synthetic binary; never executed')
}
Import-Module (Join-Path $signingRepo 'scripts/Habitat.Release.psm1') -Force
New-HabitatBuildManifest $payload
$manifestHash = (Get-FileHash -LiteralPath (Join-Path $payload 'build-manifest.json')).Hash
$generator = Join-Path $signingRepo 'scripts/prepare-signing-dossier.ps1'
$output = Join-Path $checkRoot 'report'
& $generator -PayloadDirectory $payload -OutputDirectory $output
$report = Get-Content -LiteralPath (Join-Path $output 'readiness.json') -Raw | ConvertFrom-Json
if ($report.readyToApply -ne $false -or $report.signingEnabled -ne $false -or $report.submissionAllowed -ne $false -or
    $report.networkRequestsMade -ne $false -or $report.ownBinaries.Count -ne 6 -or -not $report.payloadManifestVerified -or
    $report.excludedFromHabitatSigning.path -notcontains 'Microsoft.ThirdParty.dll' -or
    $report.pending -notcontains 'unclassified-own-binary:Habitat.Unreviewed.dll' -or
    $report.pending -notcontains 'metadata-mismatch:HABITAT.exe' -or
    (Get-FileHash -LiteralPath (Join-Path $payload 'build-manifest.json')).Hash -ne $manifestHash) {
    throw 'Offline reporting enabled signing, changed inputs or lost required diagnostics.'
}
Test-HabitatBuildManifest $payload
$fixtureMetadata = Join-Path $checkRoot 'release.json'
$fixtureCommit = git -C $signingRepo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not read test commit.' }
$fixtureVersion = (Get-Content -LiteralPath (Join-Path $signingRepo 'VERSION') -Raw).Trim()
[IO.File]::WriteAllText($fixtureMetadata, (@{ version = $fixtureVersion; runtime = 'win-x64'; sourceCommit = $fixtureCommit
    unrelatedPrivateField = 'SYNTHETIC-DO-NOT-EXPORT' } | ConvertTo-Json))
$metadataOutput = Join-Path $checkRoot 'report-with-provenance'
& $generator -PayloadDirectory $payload -ReleaseMetadataPath $fixtureMetadata -OutputDirectory $metadataOutput
if ((Get-Content -LiteralPath (Join-Path $metadataOutput 'readiness.json') -Raw).Contains('SYNTHETIC-DO-NOT-EXPORT')) {
    throw 'Unrelated input metadata leaked into a shareable report.'
}
function Expect-SigningRejection([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw 'An unsafe preparation path or corrupt payload was accepted.' }
}
Expect-SigningRejection { & $generator -PayloadDirectory $payload -OutputDirectory $output }
Expect-SigningRejection { & $generator -PayloadDirectory $payload -OutputDirectory (Join-Path $payload 'report') }
Expect-SigningRejection { & $generator -PayloadDirectory $signingRepo -OutputDirectory (Join-Path $checkRoot 'outside-input') }
Expect-SigningRejection { & $generator -PayloadDirectory $payload -OutputDirectory (Join-Path $signingRepo 'outside-artifacts') }
[IO.File]::AppendAllText((Join-Path $payload 'HABITAT.dll'), 'corrupt')
$corruptOutput = Join-Path $checkRoot 'corrupt-report'
Expect-SigningRejection { & $generator -PayloadDirectory $payload -OutputDirectory $corruptOutput }
if (Test-Path -LiteralPath $corruptOutput) { throw 'A corrupt payload created a report.' }
Write-Host 'Signing preparation checks passed: deferred state, exact own-binary scope, XML metadata, offline diagnostics, unchanged inputs, corruption and path boundaries. Synthetic artifacts only.'
