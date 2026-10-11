$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
git -C $repoRoot config core.hooksPath .githooks

$currentBranch = (git -C $repoRoot branch --show-current).Trim()
if ($currentBranch -ne "codex/autosave") {
    Write-Warning "El autopush solo actúa en codex/autosave; la rama actual es $currentBranch."
}

Write-Host "Autopush de HABITAT instalado. Cada commit en codex/autosave se validará y subirá a GitHub."
