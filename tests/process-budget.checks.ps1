param([Parameter(Mandatory)][string]$TestDirectory)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../scripts/Habitat.Process.psm1') -Force
$null = [IO.Directory]::CreateDirectory($TestDirectory)
$script = Join-Path $TestDirectory 'child process fixture.js'
[IO.File]::WriteAllText($script, @'
const { spawn } = require('node:child_process');
const child = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { windowsHide: true, stdio: 'ignore' });
console.log(child.pid);
setInterval(() => {}, 1000);
'@)
$watch = [Diagnostics.Stopwatch]::StartNew()
$process = Start-HabitatProcess -Name 'hanging-check' -FilePath 'node' -Arguments @($script) -LogDirectory $TestDirectory -TimeoutSeconds 3
$timedOut = $false
try { Wait-HabitatProcesses -Checks @($process) -Budget $watch -BudgetSeconds 8 }
catch { $timedOut = $_.Exception.Message -like 'hanging-check timed out.*Partial logs:*' }
if (-not $timedOut -or $watch.Elapsed.TotalSeconds -ge 8) { throw 'A hung check did not fail within its budget.' }
$childId = [int](Get-Content -LiteralPath $process.Output -Raw).Trim()
if ($childId -le 0 -or (Get-Process -Id $childId -ErrorAction SilentlyContinue)) {
    throw 'The timed-out check did not preserve its partial output and stop its child.'
}

$watch = [Diagnostics.Stopwatch]::StartNew()
$parallel = @()
try {
    $parallel += Start-HabitatProcess -Name 'early-exit' -FilePath 'node' -Arguments @('-e', 'console.log("early output")') -LogDirectory $TestDirectory
    $parallel += Start-HabitatProcess -Name 'later-exit' -FilePath 'node' -Arguments @('-e', 'setTimeout(() => console.error("sibling output"), 3600)') -LogDirectory $TestDirectory
    # Simulate a redirected pipe that stays open while another check runs.
    $parallel[0].Copies = [Threading.Tasks.Task[]]($parallel[0].Copies + [Threading.Tasks.Task]::Delay(3200))
} catch {
    foreach ($check in $parallel) { Close-HabitatProcess $check }
    throw
}
Wait-HabitatProcesses -Checks $parallel -Budget $watch -BudgetSeconds 12
$earlyLog = Get-Content -LiteralPath $parallel[0].Output
$siblingLog = Get-Content -LiteralPath $parallel[1].ErrorOutput
if ($earlyLog -notcontains 'early output' -or $siblingLog -notcontains 'sibling output') {
    throw 'Parallel checks did not preserve their output when a stream was slow to close.'
}
Write-Host 'Process budget checks passed: bounded wait, partial output, descendant termination, and delayed stream closure.'
