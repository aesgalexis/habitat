$ErrorActionPreference = 'Stop'
if (-not ('Habitat.Storage.GeneratedStorage' -as [type])) {
    # Windows PowerShell's compiler predates nullable reference annotations.
    $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Habitat.GeneratedStorage.cs')).Replace('#nullable disable', '')
    if ($PSVersionTable.PSEdition -eq 'Core') {
        Add-Type -TypeDefinition $source -CompilerOptions '/define:NET10_0_OR_GREATER'
    } else {
        Add-Type -TypeDefinition $source -ReferencedAssemblies System.Runtime.Serialization, System.Core, System.Xml
    }
}

function Invoke-HabitatStorageMaintenance {
    param([Parameter(Mandatory)][string]$ArtifactsRoot, [switch]$Clean, [switch]$Automatic)
    # Validation sessions already perform an explicit dry-run analysis. Avoid
    # repeatedly hashing the same installation after every build/check script.
    if ($Automatic -and ($env:HABITAT_STORAGE_DRY_RUN -eq '1' -or $env:HABITAT_STORAGE_SKIP_AUTOMATIC -eq '1')) {
        return [Habitat.Storage.StorageReport]::new()
    }
    [Habitat.Storage.GeneratedStorage]::Run($ArtifactsRoot, $Clean.IsPresent, $null, [Threading.CancellationToken]::None)
}

function Complete-HabitatGeneratedOutput {
    param([string]$ArtifactsRoot, [string]$Directory, [string]$Kind)
    try { [Habitat.Storage.GeneratedStorage]::Complete($ArtifactsRoot, $Directory, $Kind) }
    catch { Write-Warning 'The generated output was retained because its cleanup inventory could not be recorded.' }
}

Export-ModuleMember -Function Invoke-HabitatStorageMaintenance, Complete-HabitatGeneratedOutput
