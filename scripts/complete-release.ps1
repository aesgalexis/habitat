param(
    [Parameter(Mandatory)][ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')][string]$Tag,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [string]$Worktree,
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$checkout = $repo
$version = $Tag.Substring(1)
$commit = (git -C $repo rev-parse --verify "$Tag^{commit}")
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'A verified local release tag is required.' }

function Read-ReleaseApi([string]$Endpoint) {
    $json = gh api "repos/$Repository/$Endpoint"
    if ($LASTEXITCODE -ne 0) { throw "GitHub could not verify $Endpoint." }
    $json | ConvertFrom-Json
}
function Assert-PlainPath([string]$Root, [string]$Path) {
    $resolved = (Resolve-Path -LiteralPath $Path).ProviderPath
    if (-not $resolved.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'A cleanup path escaped its expected directory.'
    }
    for ($current = $resolved; $current -ne $Root; $current = Split-Path -Parent $current) {
        $item = Get-Item -LiteralPath $current -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A cleanup path contains a link.' }
    }
}

function Inspect-ReleaseCheckout {
    $tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @(git -C $checkout -c core.quotePath=false ls-files)) { $null = $tracked.Add($name) }
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect tracked release files.' }
    if (@(git -C $checkout status --porcelain=v1 --untracked-files=no).Count -or $LASTEXITCODE -ne 0) { return }
    $directories = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $directories.Push([IO.DirectoryInfo]$checkout)
    $disposable = @()
    while ($directories.Count) {
        foreach ($entry in $directories.Pop().EnumerateFileSystemInfos()) {
            $relative = $entry.FullName.Substring($checkout.Length + 1).Replace('\', '/')
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                $link = Get-Item -LiteralPath $entry.FullName -Force
                if ($link.LinkType -ne 'Junction' -or $relative -notin '.nuget', '.nuget/packages' -or
                    @($link.Target).Count -ne 1 -or [IO.Path]::GetFullPath(@($link.Target)[0]) -ne (Join-Path $repo $relative)) { return }
                $disposable += $link
            } elseif ($entry -is [IO.DirectoryInfo]) {
                $directories.Push($entry)
            } elseif ($relative -eq 'artifacts/update.lock' -and $entry.Length -eq 0) {
                $disposable += $entry
            } elseif ($relative -ne '.git' -and -not $tracked.Contains($relative)) { return }
        }
    }
    [pscustomobject]@{ Disposable = @($disposable) }
}

if ($Worktree) {
    $checkout = (Resolve-Path -LiteralPath $Worktree).ProviderPath
    Assert-PlainPath $repo $checkout
    $managed = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/release-checkouts'))
    if (-not $checkout.StartsWith($managed + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Only a registered release checkout under artifacts/release-checkouts can be retired.'
    }
    $worktrees = @(git -C $repo -c core.quotePath=false worktree list --porcelain |
        Where-Object { $_.StartsWith('worktree ') } | ForEach-Object { [IO.Path]::GetFullPath($_.Substring(9)) })
    if ($LASTEXITCODE -ne 0 -or $checkout -notin $worktrees -or (git -C $checkout rev-parse HEAD) -ne $commit) {
        throw 'The release checkout must be registered and at the published commit.'
    }
}

$publishedTag = (Read-ReleaseApi "git/ref/tags/$Tag").object
for ($depth = 0; $depth -lt 4 -and $publishedTag.type -eq 'tag'; $depth++) {
    if ($publishedTag.sha -notmatch '^[a-f0-9]{40}$') { throw 'Invalid annotated tag response.' }
    $publishedTag = (Read-ReleaseApi "git/tags/$($publishedTag.sha)").object
}
if ($publishedTag.type -ne 'commit' -or $publishedTag.sha -ne $commit) { throw 'The published tag differs from the local commit.' }
$release = Read-ReleaseApi "releases/tags/$Tag"
if ($release.tag_name -ne $Tag -or $release.draft -ne $false -or $release.prerelease -ne $false) {
    throw 'The final release is not available.'
}

$zip = "HABITAT-$version-win-x64.zip"
$exe = "HABITAT-$version-x64-setup.exe"
$expected = [ordered]@{
    $zip = "packages/$zip"
    "$zip.sha256" = "packages/$zip.sha256"
    $exe = "distribution/$version/$exe"
    "$exe.sha256" = "distribution/$version/$exe.sha256"
}
$artifacts = Join-Path $checkout 'artifacts'
$removed = @()
$eligible = @()
$retained = @()
$lock = $null
$worktreeResult = if ($Worktree) { 'Review only' } else { 'Not requested' }
try {
    if (Test-Path -LiteralPath $artifacts) {
        Assert-PlainPath $checkout $artifacts
        if ($Apply -and $env:OS -eq 'Windows_NT') {
            Import-Module (Join-Path $PSScriptRoot 'Habitat.Storage.psm1') -Force
            $lock = [Habitat.Storage.GeneratedStorage]::AcquireLock($artifacts)
            $operations = Join-Path $artifacts '.storage-operations'
            if (Test-Path -LiteralPath $operations) {
                Assert-PlainPath $artifacts $operations
                foreach ($lease in Get-ChildItem -LiteralPath $operations -File -Filter '*.json') {
                    Assert-PlainPath $artifacts $lease.FullName
                    try { [IO.File]::Open($lease.FullName, 'Open', 'ReadWrite', 'None').Dispose() }
                    catch { throw 'An artifact producer is still active. Run release cleanup after it finishes.' }
                }
            }
        }
    }
    foreach ($name in $expected.Keys) {
        $asset = @($release.assets | Where-Object { $_.name -ceq $name -and $_.state -eq 'uploaded' })
        if ($asset.Count -ne 1 -or $asset[0].digest -notmatch '^sha256:[0-9a-fA-F]{64}$') {
            throw "The release does not provide a verified digest for $name."
        }
    }
    foreach ($name in $expected.Keys) {
        $path = Join-Path $artifacts $expected[$name]
        if (-not (Test-Path -LiteralPath $path)) { continue }
        Assert-PlainPath $artifacts $path
        $asset = $release.assets | Where-Object { $_.name -ceq $name }
        $stream = [IO.File]::Open($path, 'Open', 'Read', ([IO.FileShare]::Read -bor [IO.FileShare]::Delete))
        try {
            if ($stream.Length -ne $asset.size -or (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash -ne $asset.digest.Substring(7)) {
                $retained += [pscustomobject]@{ Name = $name; Reason = 'Local bytes differ from the published asset' }
                continue
            }
            $eligible += $name
            if ($Apply) { [IO.File]::Delete($path); $removed += $name }
        } finally { $stream.Dispose() }
    }
    if ($Worktree -and $Apply) {
        $worktreeResult = 'Retained because the checkout contains local or ignored files'
        $plan = Inspect-ReleaseCheckout
        if ($plan) {
            if ($lock) { $lock.Dispose(); $lock = $null }
            foreach ($entry in $plan.Disposable) {
                if ($entry.LinkType -eq 'Junction') {
                    # Remove the directory entry only. Its shared NuGet target stays in place.
                    [IO.Directory]::Delete($entry.FullName, $false)
                } else { [IO.File]::Delete($entry.FullName) }
            }
            $remaining = @(git -C $checkout status --porcelain=v1 --untracked-files=all)
            if ($LASTEXITCODE -ne 0) { throw 'Could not recheck the release checkout.' }
            if ((Inspect-ReleaseCheckout) -and $remaining.Count -eq 0) {
                git -C $repo worktree remove -- $checkout
                if ($LASTEXITCODE -ne 0) { throw 'Git could not retire the clean release checkout.' }
                $worktreeResult = 'Removed'
            }
        }
    }
} finally { if ($lock) { $lock.Dispose() } }
[pscustomobject]@{
    Tag = $Tag; Commit = $commit; Published = $release.html_url
    Eligible = @($eligible); Removed = @($removed); Retained = @($retained); Worktree = $worktreeResult
} | ConvertTo-Json -Depth 4
