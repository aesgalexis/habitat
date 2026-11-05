param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$InstallerPayloadRoot,
    [Parameter(Mandatory)][string]$ExpectedClientPath
)
$ErrorActionPreference = 'Stop'
$clientFile = Get-Item -LiteralPath $ExpectedClientPath
if ($clientFile.Length -gt 65536) { throw 'The release client configuration is too large.' }
$expected = [IO.File]::ReadAllText($clientFile.FullName)
$client = $expected | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($client.installed.client_id) -or
    [string]::IsNullOrWhiteSpace($client.installed.client_secret)) {
    throw 'The release client configuration is invalid.'
}
function Assert-EmbeddedDriveClient([byte[]]$Bytes, [string]$Format) {
    if (-not [Text.Encoding]::UTF8.GetString($Bytes).Contains($expected)) {
        throw "$Format is missing the intended Google Drive desktop client."
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $entry = $zip.GetEntry('artifacts/staged/app/HABITAT.dll')
    if ($null -eq $entry) { throw 'The ZIP application is missing.' }
    $inputStream = $entry.Open()
    $outputStream = [IO.MemoryStream]::new()
    try {
        $inputStream.CopyTo($outputStream)
        Assert-EmbeddedDriveClient $outputStream.ToArray() 'ZIP'
    } finally { $inputStream.Dispose(); $outputStream.Dispose() }
} finally { $zip.Dispose() }
$payloads = @(Get-ChildItem -LiteralPath $InstallerPayloadRoot -Directory | ForEach-Object {
    $path = Join-Path $_.FullName 'payload/HABITAT.dll'
    if (Test-Path -LiteralPath $path -PathType Leaf) { $path }
})
if ($payloads.Count -ne 1) { throw 'Expected one EXE installer payload.' }
Assert-EmbeddedDriveClient ([IO.File]::ReadAllBytes($payloads[0])) 'EXE'
Write-Host 'ZIP and EXE payloads include the intended Google Drive desktop client.'
