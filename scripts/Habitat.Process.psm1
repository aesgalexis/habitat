function Start-HabitatProcess {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$LogDirectory,
        [int]$TimeoutSeconds = 120
    )
    $executable = (Get-Command $FilePath -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $null = [IO.Directory]::CreateDirectory($LogDirectory)
    $output = Join-Path $LogDirectory "$Name.stdout.log"
    $errors = Join-Path $LogDirectory "$Name.stderr.log"
    # .NET Framework accepts a Windows command line, not an argument array.
    $quoted = foreach ($argument in $Arguments) {
        '"' + ($argument -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
    }
    $start = [Diagnostics.ProcessStartInfo]::new($executable, ($quoted -join ' '))
    $start.WorkingDirectory = (Get-Location).ProviderPath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = 'Hidden'
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $streams = @()
    $process = $null
    try {
        $streams += [IO.File]::Open($output, 'Create', 'Write', 'Read')
        $streams += [IO.File]::Open($errors, 'Create', 'Write', 'Read')
        $process = [Diagnostics.Process]::Start($start)
        [pscustomobject]@{
            Name = $Name; Process = $process; Output = $output; ErrorOutput = $errors; Watch = $watch
            TimeoutSeconds = $TimeoutSeconds; Streams = $streams
            Copies = [Threading.Tasks.Task[]]@(
                $process.StandardOutput.BaseStream.CopyToAsync($streams[0])
                $process.StandardError.BaseStream.CopyToAsync($streams[1])
            )
        }
    } catch {
        if ($process) { try { Stop-HabitatProcess @{ Process = $process; Name = $Name } } finally { $process.Dispose() } }
        foreach ($stream in $streams) { $stream.Dispose() }
        throw
    }
}

function Close-HabitatProcess {
    param([Parameter(Mandatory)]$Check)
    try { Stop-HabitatProcess $Check }
    catch { Write-Warning "Could not stop $($Check.Name): $_" }
    finally {
        try {
            if (-not [Threading.Tasks.Task]::WaitAll($Check.Copies, 2000)) {
                Write-Warning "$($Check.Name) left an open output stream. Partial logs: $($Check.Output) and $($Check.ErrorOutput)"
            }
        }
        catch { Write-Warning "Could not capture all output for $($Check.Name)." }
        finally {
            foreach ($stream in $Check.Streams) { $stream.Dispose() }
            $Check.Process.Dispose()
        }
    }
}

function Stop-HabitatProcess {
    param([Parameter(Mandatory)]$Check)
    if ($Check.Process.HasExited) { return }
    try {
        # Restrict termination to the process tree started for this check.
        & (Join-Path $env:SystemRoot 'System32/taskkill.exe') /PID $Check.Process.Id /T /F 2>$null | Out-Null
    } catch { Write-Warning "Could not terminate the full process tree for $($Check.Name)." }
    if (-not $Check.Process.WaitForExit(3000)) {
        $Check.Process.Kill()
        $null = $Check.Process.WaitForExit(1000)
    }
}

function Wait-HabitatProcesses {
    param(
        [Parameter(Mandatory)][object[]]$Checks,
        [Parameter(Mandatory)][Diagnostics.Stopwatch]$Budget,
        [Parameter(Mandatory)][int]$BudgetSeconds
    )
    $pending = [Collections.Generic.List[object]]::new()
    foreach ($check in $Checks) { $pending.Add($check) }
    try {
        while ($pending.Count) {
            foreach ($check in $pending.ToArray()) {
                if ($check.Process.HasExited) {
                    if ($check.Process.ExitCode -ne 0) {
                        throw "$($check.Name) failed (exit $($check.Process.ExitCode)). Logs: $($check.Output) and $($check.ErrorOutput)"
                    }
                    Write-Host ('{0} completed in {1:N1}s.' -f $check.Name, $check.Watch.Elapsed.TotalSeconds)
                    $null = $pending.Remove($check)
                    continue
                }
                if ($Budget.Elapsed.TotalSeconds -ge $BudgetSeconds -or
                    $check.Watch.Elapsed.TotalSeconds -ge $check.TimeoutSeconds) {
                    throw "$($check.Name) timed out. Partial logs: $($check.Output) and $($check.ErrorOutput)"
                }
            }
            if ($pending.Count) { Start-Sleep -Milliseconds 100 }
        }
    } finally {
        # A sibling or descendant can still hold an inherited pipe after the root process exits.
        foreach ($check in $pending) {
            try { Stop-HabitatProcess $check }
            catch { Write-Warning "Could not stop $($check.Name): $_" }
        }
        foreach ($check in $Checks) {
            Close-HabitatProcess $check
            foreach ($log in $check.Output, $check.ErrorOutput) {
                Get-Content -LiteralPath $log -Tail 200 | ForEach-Object { Write-Host $_ }
            }
        }
    }
}

Export-ModuleMember -Function Start-HabitatProcess,Close-HabitatProcess,Wait-HabitatProcesses
