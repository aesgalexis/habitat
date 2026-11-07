param([Parameter(Mandatory)][string]$TestDirectory)
$ErrorActionPreference = 'Stop'
$null = New-Item -ItemType Directory -Path $TestDirectory
$project = Join-Path $TestDirectory 'project'
New-Item -ItemType Directory -Path $project, (Join-Path $project 'bin/old/updater'), (Join-Path $project 'obj/old') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $project 'Fixture.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
[IO.File]::WriteAllText((Join-Path $project 'Fixture.cs'), 'public sealed class Fixture { }')
[IO.File]::WriteAllText((Join-Path $project 'bin/old/updater/Stale.cs'), '#error A previous output was compiled as source.')
[IO.File]::WriteAllText((Join-Path $project 'obj/old/Stale.g.cs'), '#error A previous intermediate output was compiled as source.')
dotnet build (Join-Path $project 'Fixture.csproj') --configuration Release --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'A default build included stale output sources.' }
dotnet build (Join-Path $project 'Fixture.csproj') --configuration Release --no-restore "-p:BaseOutputPath=$TestDirectory/isolated/"
if ($LASTEXITCODE -ne 0) { throw 'Changing BaseOutputPath included stale output sources.' }
if (-not (Test-Path -LiteralPath (Join-Path $project 'bin/old/updater/Stale.cs'))) { throw 'The regression check must preserve the previous output.' }
Write-Host 'Build checks passed: default and isolated outputs ignore existing bin/obj sources without deleting them.'
