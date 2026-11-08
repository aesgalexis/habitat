$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Habitat.Release.psm1')
Import-Module (Join-Path $PSScriptRoot 'Habitat.Storage.psm1')

function Assert-HabitatUpdatePath([string]$Root, [string]$Path) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $child = [IO.Path]::GetFullPath($Path)
    if (-not $child.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update path escapes its directory.' }
    Assert-HabitatNoLinks $child
    return $child
}

function Get-HabitatInstallerArguments([string]$Directory, [string]$Log) {
    # Windows paths cannot contain quotes. Keep arguments separate from commands.
    if ($Directory.Contains('"') -or $Log.Contains('"')) { throw 'Invalid installer path.' }
    return '/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NORESTARTAPPLICATIONS /DIR="' + $Directory + '" /LOG="' + $Log + '"'
}

function Install-HabitatZipUpdate {
    param([string]$PackageRoot, [string]$InstallRoot, [string]$Version,
        [scriptblock]$StartRelease, [scriptblock]$StopRelease, [scriptblock]$BeforeActivate)
    $metadata = Get-Content -LiteralPath (Join-Path $PackageRoot 'release.json') -Raw | ConvertFrom-Json
    if ($metadata.version -ne $Version -or $metadata.runtime -ne 'win-x64') { throw 'Update metadata differs from the release.' }
    $payload = Join-Path $PackageRoot 'artifacts/staged/app'
    Test-HabitatBuildManifest $payload
    $artifacts = Join-Path $InstallRoot 'artifacts'
    $stage = Assert-HabitatUpdatePath $artifacts (Join-Path $artifacts ('staged/update-' + [guid]::NewGuid().ToString('N')))
    [IO.Directory]::CreateDirectory($artifacts) | Out-Null
    $operation = [Habitat.Storage.GeneratedStorage]::BeginOperation($artifacts, @($stage))
    try {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Get-ChildItem -LiteralPath $payload -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $stage -Recurse }
    # Keep the matching integrations with the active binaries. In a development
    # checkout this never replaces source files, scripts or the working tree.
    foreach ($mapping in @(@('extension', 'browser-extension'), @('vscode-extension', 'vscode-extension'))) {
        Copy-Item -LiteralPath (Join-Path $PackageRoot $mapping[0]) -Destination (Join-Path $stage $mapping[1]) -Recurse
    }
    Copy-Item -LiteralPath (Join-Path $PackageRoot 'release.json') -Destination $stage
    New-HabitatBuildManifest $stage
    $installed = Install-HabitatRelease -ArtifactsRoot $artifacts -StageDirectory $stage -BeforeActivate $BeforeActivate -DeferStorageMaintenance `
        -AfterActivate $StartRelease -BeforeRollback $StopRelease -AfterRollback $StartRelease
    } finally { $operation.Dispose() }
    try { $null = Invoke-HabitatStorageMaintenance -ArtifactsRoot $artifacts -Clean -Automatic }
    catch { Write-Warning 'Generated storage maintenance could not finish. Installation succeeded.' }
    return $installed
}

Export-ModuleMember -Function Assert-HabitatUpdatePath, Get-HabitatInstallerArguments, Install-HabitatZipUpdate
