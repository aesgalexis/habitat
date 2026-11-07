param([Parameter(Mandatory)][string]$ChecksAssembly, [Parameter(Mandatory)][string]$TestDirectory)
$ErrorActionPreference = 'Stop'
$payloadRepo = Split-Path -Parent $PSScriptRoot
$root = (New-Item -ItemType Directory -Path $TestDirectory).FullName
$reference = Join-Path $root 'reference'
$payload = Join-Path $root 'packaged payload'
New-Item -ItemType Directory -Path $reference, $payload -Force | Out-Null
$ChecksAssembly = (Resolve-Path -LiteralPath $ChecksAssembly).Path
# This checks assembly embeds a public synthetic OAuth fixture. It is copied as
# data, never loaded as an application or executed under the HABITAT name.
Copy-Item -LiteralPath $ChecksAssembly -Destination (Join-Path $reference 'HABITAT.dll')
foreach ($name in 'HABITAT.exe','HABITAT.Hotkey.exe','HABITAT.Hotkey.dll','Habitat.Core.dll','Habitat.Activation.dll') {
    [IO.File]::WriteAllText((Join-Path $reference $name), 'synthetic bytes for ' + $name)
}
Get-ChildItem -LiteralPath $reference -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $payload }
$verifyPayload = Join-Path $payloadRepo 'scripts/check-release-payload.ps1'
$clientFixture = Join-Path $payloadRepo 'tests/fixtures/drive-client.synthetic.json'
& $verifyPayload -PayloadDirectory $payload -ReferenceDirectory $reference -RequireDrive -NoRestore -ExpectedDriveClientPath $clientFixture
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $root 'payload.zip'
$unpacked = Join-Path $root 'unpacked'
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip)
[IO.Compression.ZipFile]::ExtractToDirectory($zip, $unpacked)
& $verifyPayload -PayloadDirectory $unpacked -ReferenceDirectory $reference -RequireDrive -NoRestore
function Expect-PayloadRejection([string]$Directory, [string]$Reference, [string]$ExpectedClientPath) {
    $rejected = $false
    try { & $verifyPayload -PayloadDirectory $Directory -ReferenceDirectory $Reference -RequireDrive -NoRestore -ExpectedDriveClientPath $ExpectedClientPath *> (Join-Path $root 'expected-rejection.log') }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'An invalid or mismatched release payload was accepted.' }
}
$differentClient = Join-Path $root 'different-client.json'
[IO.File]::WriteAllText($differentClient, [IO.File]::ReadAllText($clientFixture).Replace('synthetic-only.apps', 'different-test.apps'))
Expect-PayloadRejection $unpacked $reference $differentClient
[IO.File]::WriteAllText((Join-Path $payload 'Habitat.Activation.dll'), 'modified packaged binary')
Expect-PayloadRejection $payload $reference
Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $ChecksAssembly) 'Habitat.Core.dll') -Destination (Join-Path $payload 'HABITAT.dll') -Force
Expect-PayloadRejection $payload $null
& $verifyPayload -PayloadDirectory $payload -NoRestore
Write-Host 'Payload checks passed: shared binaries, ZIP roundtrip, rejected binary changes, expected Drive client identity and optional custom builds. Synthetic files only.'
