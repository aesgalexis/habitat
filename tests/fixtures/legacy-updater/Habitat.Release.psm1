$ErrorActionPreference = 'Stop'

function Assert-HabitatChildPath([string]$Root, [string]$Path) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $childPath = [IO.Path]::GetFullPath($Path)
    if (-not $childPath.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release path must stay inside $rootPath."
    }
    $walk = $childPath
    while ($walk -and $walk.Length -ge $rootPath.Length) {
        if ((Test-Path -LiteralPath $walk) -and ((Get-Item -LiteralPath $walk -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Release paths cannot contain filesystem links.'
        }
        $walk = Split-Path -Parent $walk
    }
    return $childPath
}

function Write-HabitatAtomicText([string]$Path, [string]$Value) {
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
        $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        if ([IO.File]::Exists($Path)) {
            for ($attempt = 0; ; $attempt++) {
                try { [IO.File]::Replace($temporary, $Path, [NullString]::Value); break }
                catch {
                    $cause = $_.Exception
                    while ($cause.InnerException) { $cause = $cause.InnerException }
                    if ($attempt -ge 4 -or ($cause.HResult -band 65535) -notin 32, 33, 1175 -or
                        -not [IO.File]::Exists($temporary) -or -not [IO.File]::Exists($Path)) { throw }
                    Start-Sleep -Milliseconds (10 * [math]::Pow(2, $attempt))
                }
            }
        }
        else { [IO.File]::Move($temporary, $Path) }
    } finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}

function Get-HabitatReleaseFiles([string]$Directory) {
    $root = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/')
    foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Release contents cannot contain filesystem links.' }
        if (-not $item.PSIsContainer -and $item.FullName -ne (Join-Path $root 'build-manifest.json')) {
            [pscustomobject]@{
                path = $item.FullName.Substring($root.Length + 1).Replace('\', '/')
                length = $item.Length
                sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
            }
        }
    }
}

function New-HabitatBuildManifest([string]$Directory) {
    $manifest = [ordered]@{ version = 1; files = @(Get-HabitatReleaseFiles $Directory | Sort-Object path) }
    Write-HabitatAtomicText (Join-Path $Directory 'build-manifest.json') ($manifest | ConvertTo-Json -Depth 5)
}

function Test-HabitatBuildManifest([string]$Directory) {
    $manifest = Get-Content -LiteralPath (Join-Path $Directory 'build-manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.version -ne 1 -or @($manifest.files).Count -eq 0) { throw 'Invalid build manifest.' }
    $actual = @(Get-HabitatReleaseFiles $Directory | Sort-Object path)
    $expected = @($manifest.files | Sort-Object path)
    if ($actual.Count -ne $expected.Count) { throw 'Build contents do not match the manifest.' }
    $names = @{}
    for ($index = 0; $index -lt $actual.Count; $index++) {
        $entry = $expected[$index]
        if ($names.ContainsKey($entry.path) -or $entry.path -ne $actual[$index].path -or
            $entry.length -ne $actual[$index].length -or $entry.sha256 -ne $actual[$index].sha256) {
            throw 'Build verification failed: missing, duplicate or damaged file.'
        }
        $names[$entry.path] = $true
    }
    foreach ($required in 'HABITAT.exe', 'HABITAT.dll', 'Habitat.Core.dll') {
        if (-not $names.ContainsKey($required)) { throw "Build is missing $required." }
    }
}

function Get-HabitatActiveDirectory([string]$ArtifactsRoot) {
    $pointer = Join-Path $ArtifactsRoot 'current-release.txt'
    if (-not (Test-Path -LiteralPath $pointer)) { return (Join-Path $ArtifactsRoot 'win-x64') }
    $relative = (Get-Content -LiteralPath $pointer -Raw).Trim()
    if ($relative -notmatch '^releases[/\\]release-[a-f0-9]{32}$' -and $relative -ne 'win-x64') {
        throw 'Invalid active release pointer.'
    }
    return (Assert-HabitatChildPath $ArtifactsRoot (Join-Path $ArtifactsRoot $relative))
}

function Install-HabitatRelease {
    param(
        [Parameter(Mandatory)][string]$ArtifactsRoot,
        [Parameter(Mandatory)][string]$StageDirectory,
        [scriptblock]$BeforeActivate = {},
        [scriptblock]$AfterActivate = {},
        [scriptblock]$BeforeRollback = {},
        [scriptblock]$AfterRollback = {}
    )
    $stage = Assert-HabitatChildPath $ArtifactsRoot $StageDirectory
    $pointer = Join-Path $ArtifactsRoot 'current-release.txt'
    $previousPointer = Join-Path $ArtifactsRoot 'previous-release.txt'
    $lock = [IO.File]::Open((Join-Path $ArtifactsRoot 'update.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $activated = $false
    $activationPrepared = $false
    try {
        Test-HabitatBuildManifest $stage
        $previous = Get-HabitatActiveDirectory $ArtifactsRoot
        if ($stage -eq $previous) { throw 'The stage must be separate from the active release.' }
        $hadPointer = Test-Path -LiteralPath $pointer
        $oldPointer = if ($hadPointer) { [IO.File]::ReadAllText($pointer) } else { 'win-x64' }
        $relative = 'releases\release-' + [guid]::NewGuid().ToString('N')
        $release = Assert-HabitatChildPath $ArtifactsRoot (Join-Path $ArtifactsRoot $relative)
        New-Item -ItemType Directory -Path $release -Force | Out-Null
        foreach ($item in Get-ChildItem -LiteralPath $stage -Force) {
            Copy-Item -LiteralPath $item.FullName -Destination $release -Recurse -Force
        }
        Test-HabitatBuildManifest $release
        & $BeforeActivate $previous
        $activationPrepared = $true
        Write-HabitatAtomicText $previousPointer $oldPointer
        Write-HabitatAtomicText $pointer $relative
        $activated = $true
        & $AfterActivate $release
        return $release
    } catch {
        $failure = $_
        if ($activated) {
            try { & $BeforeRollback $release }
            finally {
                if ($hadPointer) { Write-HabitatAtomicText $pointer $oldPointer }
                else { [IO.File]::Delete($pointer) }
            }
            & $AfterRollback $previous
        } elseif ($activationPrepared) {
            & $AfterRollback $previous
        }
        throw $failure
    } finally { $lock.Dispose() }
}

Export-ModuleMember -Function New-HabitatBuildManifest, Test-HabitatBuildManifest, Get-HabitatActiveDirectory, Install-HabitatRelease
