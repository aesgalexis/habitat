param([switch]$Clean)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Habitat.Storage.psm1') -Force
$report = Invoke-HabitatStorageMaintenance -ArtifactsRoot (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts') -Clean:$Clean
$report | ConvertTo-Json -Depth 4
