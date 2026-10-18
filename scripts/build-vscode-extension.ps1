param()
$ErrorActionPreference = 'Stop'
$extensionRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'vscode-extension'
$extensionOutput = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\vscode'
$package = Get-Content -LiteralPath (Join-Path $extensionRoot 'package.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $extensionOutput -Force | Out-Null
$vsixPath = Join-Path $extensionOutput ("habitat-terminal-" + $package.version + '.vsix')
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($vsixPath, [IO.FileMode]::Create)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
function Add-ExtensionEntry([string]$Name, [string]$Text) {
    $writer = New-Object IO.StreamWriter($archive.CreateEntry($Name).Open(), (New-Object Text.UTF8Encoding($false)))
    try { $writer.Write($Text) } finally { $writer.Dispose() }
}
try {
    Add-ExtensionEntry '[Content_Types].xml' '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="json" ContentType="application/json"/><Default Extension="js" ContentType="application/javascript"/><Default Extension="md" ContentType="text/markdown"/><Default Extension="vsixmanifest" ContentType="text/xml"/></Types>'
    Add-ExtensionEntry 'extension.vsixmanifest' @"
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata>
    <Identity Language="en-US" Id="$($package.name)" Version="$($package.version)" Publisher="$($package.publisher)" />
    <DisplayName>HABITAT Terminal</DisplayName>
    <Description xml:space="preserve">Fresh VS Code terminals for HABITAT.</Description>
    <Categories>Other</Categories>
    <Properties>
      <Property Id="Microsoft.VisualStudio.Code.Engine" Value="$($package.engines.vscode)" />
      <Property Id="Microsoft.VisualStudio.Code.ExtensionKind" Value="ui" />
      <Property Id="Microsoft.VisualStudio.Code.ExecutesCode" Value="true" />
    </Properties>
  </Metadata>
  <Installation><InstallationTarget Id="Microsoft.VisualStudio.Code" /></Installation>
  <Dependencies />
  <Assets><Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" Addressable="true" /></Assets>
</PackageManifest>
"@
    foreach ($file in 'package.json', 'extension.js', 'terminal-runs.js', 'README.md') {
        Add-ExtensionEntry ("extension/" + $file) (Get-Content -LiteralPath (Join-Path $extensionRoot $file) -Raw)
    }
} finally { $archive.Dispose(); $stream.Dispose() }
Write-Host "VS Code extension prepared: $vsixPath"
Write-Host "Install from your normal Windows session: code --install-extension `"$vsixPath`" --force"
