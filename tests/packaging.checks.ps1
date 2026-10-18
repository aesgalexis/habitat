$ErrorActionPreference = 'Stop'
$packagingRepo = Split-Path -Parent $PSScriptRoot
$checksRoot = Join-Path $packagingRepo ('artifacts/packaging-checks/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checksRoot -Force | Out-Null
$fixture = Join-Path $checksRoot 'HABITAT-0.1.0-x64-setup.exe'
[IO.File]::WriteAllBytes($fixture, [byte[]](1, 2, 3, 4))
$generator = Join-Path $packagingRepo 'scripts/new-winget-manifest.ps1'
$arguments = @{
    Version = '0.1.0'
    InstallerPath = $fixture
    InstallerUrl = 'https://example.com/releases/v0.1.0/HABITAT-0.1.0-x64-setup.exe'
    OutputDirectory = Join-Path $checksRoot 'first'
}
& $generator @arguments
$manifestDirectory = Join-Path $arguments.OutputDirectory 'manifests/a/aesgalexis/HABITAT/0.1.0'
$installerManifest = Get-Content -LiteralPath (Join-Path $manifestDirectory 'aesgalexis.HABITAT.installer.yaml') -Raw
if (@(Get-ChildItem -LiteralPath $manifestDirectory -Filter '*.yaml').Count -ne 3 -or
    -not $installerManifest.Contains((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash)) {
    throw 'Manifests must use the exact installer hash and the three-file format.'
}
function Expect-Rejection([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unsafe manifest input was accepted.' }
}
Expect-Rejection { & $generator @arguments }
$arguments.OutputDirectory = Join-Path $checksRoot 'invalid'
$arguments.InstallerUrl = 'http://example.com/HABITAT-0.1.0-x64-setup.exe'
Expect-Rejection { & $generator @arguments }
$arguments.InstallerUrl = 'https://example.com/latest/HABITAT-0.1.0-x64-setup.exe'
Expect-Rejection { & $generator @arguments }
$arguments.InstallerUrl = 'https://example.com/releases/HABITAT-0.2.0-x64-setup.exe'
Expect-Rejection { & $generator @arguments }
if (Test-Path -LiteralPath $arguments.OutputDirectory) { throw 'Rejected inputs must not write manifests.' }
$arguments.InstallerUrl = 'https://example.com/releases/v0.1.0/HABITAT-0.1.0-x64-setup.exe'
$arguments.OutputDirectory = Join-Path $checksRoot 'second'
[IO.File]::WriteAllBytes($fixture, [byte[]](5, 6, 7, 8))
& $generator @arguments
$changed = Get-Content -LiteralPath (Join-Path $arguments.OutputDirectory 'manifests/a/aesgalexis/HABITAT/0.1.0/aesgalexis.HABITAT.installer.yaml') -Raw
if ($changed.Contains(($installerManifest -split 'InstallerSha256: ')[1].Split("`n")[0].Trim()) -or
    -not $changed.Contains((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash)) {
    throw 'Regenerated manifests must reflect changed installer bytes.'
}
Write-Host 'Packaging checks passed: SHA256, versioned HTTPS URLs, rejected inputs and overwrite protection. Synthetic files only.'
