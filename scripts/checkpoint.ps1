param(
    [string]$Message = "chore(autosave): checkpoint $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

git -C $repoRoot add --all
git -C $repoRoot diff --cached --quiet
if ($LASTEXITCODE -eq 0) {
    Write-Host "No hay cambios para guardar."
    exit 0
}

git -C $repoRoot commit -m $Message
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo crear el checkpoint."
}

Write-Host "Checkpoint creado. El hook post-commit ha intentado subirlo automáticamente."
