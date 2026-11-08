param([Parameter(Mandatory)][string]$TestDirectory)
$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $PSScriptRoot
$artifacts = (Resolve-Path -LiteralPath (Join-Path $source 'artifacts')).ProviderPath
$fixture = [IO.Path]::GetFullPath((Join-Path $TestDirectory 'retirement-fixture'))
if (-not $fixture.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $fixture)) { throw 'Use a new release retirement fixture inside artifacts.' }
$null = [IO.Directory]::CreateDirectory((Join-Path $fixture 'scripts'))
foreach ($file in 'complete-release.ps1', 'Habitat.Storage.psm1', 'Habitat.GeneratedStorage.cs') {
    Copy-Item -LiteralPath (Join-Path $source "scripts/$file") -Destination (Join-Path $fixture 'scripts')
}
[IO.File]::WriteAllLines((Join-Path $fixture '.gitignore'), @('artifacts/', '.nuget/'))
[IO.File]::WriteAllText((Join-Path $fixture 'VERSION'), '0.99.0')
git -C $fixture init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the release fixture.' }
git -C $fixture add --all
if ($LASTEXITCODE -ne 0) { throw 'Could not stage the release fixture.' }
git -C $fixture -c user.name=Fixture -c user.email=fixture@local commit --quiet -m 'Release retirement fixture'
if ($LASTEXITCODE -ne 0) { throw 'Could not commit the release fixture.' }
git -C $fixture tag v0.99.0
if ($LASTEXITCODE -ne 0) { throw 'Could not tag the release fixture.' }
$fixtureCommit = git -C $fixture rev-parse HEAD

$fixtureAssets = foreach ($name in 'HABITAT-0.99.0-win-x64.zip', 'HABITAT-0.99.0-win-x64.zip.sha256',
    'HABITAT-0.99.0-x64-setup.exe', 'HABITAT-0.99.0-x64-setup.exe.sha256') {
    $assetDirectory = if ($name -like '*.zip*') { 'artifacts/packages' } else { 'artifacts/distribution/0.99.0' }
    $folder = Join-Path $fixture $assetDirectory
    $null = [IO.Directory]::CreateDirectory($folder)
    $path = Join-Path $folder $name
    [IO.File]::WriteAllText($path, "published fixture $name")
    [pscustomobject]@{
        name = $name; state = 'uploaded'; size = (Get-Item -LiteralPath $path).Length
        digest = 'sha256:' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
}
function gh {
    $global:LASTEXITCODE = 0
    if ($args[0] -ne 'api') { throw 'Unexpected GitHub command in release fixture.' }
    if ($args[1] -eq 'repos/example/habitat/git/ref/tags/v0.99.0') {
        @{ object = @{ type = 'commit'; sha = $fixtureCommit } } | ConvertTo-Json -Depth 3
    } elseif ($args[1] -eq 'repos/example/habitat/releases/tags/v0.99.0') {
        @{ tag_name = 'v0.99.0'; draft = $false; prerelease = $false; html_url = 'https://example.invalid/release'; assets = $fixtureAssets } | ConvertTo-Json -Depth 4
    } else { throw 'Unexpected GitHub endpoint in release fixture.' }
}
$script = Join-Path $fixture 'scripts/complete-release.ps1'
$review = & $script -Tag v0.99.0 -Repository example/habitat | ConvertFrom-Json
if ($review.Eligible.Count -ne 4 -or $review.Removed.Count) { throw 'Review did not classify exact release copies without removing them.' }
$zip = Join-Path $fixture 'artifacts/packages/HABITAT-0.99.0-win-x64.zip'
[IO.File]::WriteAllText($zip, 'different local package')
$applied = & $script -Tag v0.99.0 -Repository example/habitat -Apply | ConvertFrom-Json
if ($applied.Removed.Count -ne 3 -or $applied.Retained.Count -ne 1 -or [IO.File]::ReadAllText($zip) -ne 'different local package') {
    throw 'Retirement did not preserve the unmatched package.'
}

$worktree = Join-Path $fixture 'artifacts/release-checkouts/finished'
git -C $fixture worktree add --quiet --detach $worktree v0.99.0
if ($LASTEXITCODE -ne 0) { throw 'Could not create the release checkout fixture.' }
$checkoutArtifacts = Join-Path $worktree 'artifacts'
$null = [IO.Directory]::CreateDirectory($checkoutArtifacts)
$config = Join-Path $checkoutArtifacts 'local-config.json'
[IO.File]::WriteAllText($config, 'synthetic unique settings')
$kept = & $script -Tag v0.99.0 -Repository example/habitat -Worktree $worktree -Apply | ConvertFrom-Json
if ($kept.Worktree -notlike 'Retained*' -or [IO.File]::ReadAllText($config) -ne 'synthetic unique settings') { throw 'A checkout with ignored files was not preserved.' }
[IO.File]::Delete($config)
$cache = Join-Path $fixture '.nuget'
$null = [IO.Directory]::CreateDirectory($cache)
$sentinel = Join-Path $cache 'shared-cache.txt'
[IO.File]::WriteAllText($sentinel, 'shared dependency')
$null = New-Item -ItemType Junction -Path (Join-Path $worktree '.nuget') -Target $cache
$closed = & $script -Tag v0.99.0 -Repository example/habitat -Worktree $worktree -Apply | ConvertFrom-Json
if ($closed.Worktree -ne 'Removed' -or (Test-Path -LiteralPath $worktree) -or [IO.File]::ReadAllText($sentinel) -ne 'shared dependency') {
    throw 'The clean checkout was not retired while preserving the shared cache.'
}
$resolved = (Resolve-Path -LiteralPath $fixture).ProviderPath
if ($resolved -ne $fixture -or -not $resolved.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The completed synthetic fixture escaped artifacts.'
}
Remove-Item -LiteralPath $resolved -Recurse -Force
Write-Host 'Release retirement checks passed: exact published bytes, unique files, clean worktrees, and shared cache.'
