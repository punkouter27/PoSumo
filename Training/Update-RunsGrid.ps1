<#
.SYNOPSIS
    Note how a training run ended and rebuild the training runs grid
    (Training/results/runs.html, written by benchmark/runs.py).

.DESCRIPTION
    Called by the launchers when an mlagents-learn run ends - finished, stopped or
    crashed. It never throws and never touches the trainer: a failure only prints a
    warning, so the grid can never fail or change a training run.

    Two ways in:
      * -TrainerPid: wait for that trainer process to exit, then record it. The
        fire-and-forget launchers (Start-Training.ps1, Start-StaminaExtension.ps1)
        start this script detached and hidden right after the trainer, so the row is
        written when the run really ends, even after the launching shell is closed.
      * no -TrainerPid: record now (Run-Unique01.ps1, which waits on its trainers).

    How the run ended is -Ended when given, else read from the trainer's exit code.

.EXAMPLE
    ./Training/Update-RunsGrid.ps1 -RunId matt_real01 -Launcher Training/Start-Training.ps1 -TrainerPid 1234
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunId,
    [Parameter(Mandatory)][string]$Launcher,
    [string]$Ended,
    [int]$TrainerPid = 0,
    [string]$ExitCode,
    # Appended to how the run ended, e.g. "launcher interrupted (Ctrl+C)".
    [string]$Note
)

function Get-EndedText([string]$code) {
    if ($code -eq '') { return 'trainer exited (exit code unknown)' }
    switch ([int64]$code) {
        0 { return 'trainer exited (code 0)' }
        # STATUS_CONTROL_C_EXIT: Ctrl+C, or the console window closed (Stop-Training.ps1 closes it).
        -1073741510 { return 'stopped (Ctrl+C / window closed)' }
        3221225786 { return 'stopped (Ctrl+C / window closed)' }
        default { return "trainer crashed (exit code $code)" }
    }
}

try {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    if ($TrainerPid -gt 0) {
        $proc = Get-Process -Id $TrainerPid -ErrorAction SilentlyContinue
        if ($proc) {
            try { $null = $proc.Handle } catch { }   # keep a handle so ExitCode is readable after exit
            $proc.WaitForExit()
            try { $ExitCode = [string]$proc.ExitCode } catch { $ExitCode = '' }
        }
    }
    if (-not $Ended) { $Ended = Get-EndedText $ExitCode }
    if ($Note) { $Ended = "$Ended; $Note" }

    $python = Join-Path $repoRoot 'Training/venv/Scripts/python.exe'
    if (-not (Test-Path $python)) { $python = 'python' }
    Push-Location $repoRoot
    try {
        & $python (Join-Path $repoRoot 'benchmark/runs.py') --record $RunId --ended $Ended --launcher $Launcher
        if ($LASTEXITCODE -ne 0) { Write-Warning "runs grid not rebuilt for ${RunId} (benchmark/runs.py exit $LASTEXITCODE)" }
    }
    finally { Pop-Location }
}
catch {
    Write-Warning "runs grid not rebuilt for ${RunId}: $_"
}
