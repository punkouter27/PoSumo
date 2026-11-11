<#
.SYNOPSIS
    Unattended multi-agent RL optimization loop for PoSumo.
    Runs Pass 1 (initial Unique01 promotion if any fighter lacks a run) and then
    continuous round-robin --resume waves to maximize ELO and reward convergence,
    exporting production ONNX weights and updating the HTML report after every wave.
#>
param(
    [double]$TotalHours = 5.3,
    [int]$Pass1Minutes = 12,
    [int]$ResumeWaveMinutes = 15,
    [int]$Concurrent = 2,
    [int]$NumEnvs = 2
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
$venvPython = Join-Path $repoRoot 'Training/venv/Scripts/python.exe'
$startTime = Get-Date
$deadline = $startTime.AddHours($TotalHours)
$logMd = Join-Path $repoRoot 'DOCS/20261110/extended_8h_log.md'

function Write-CampaignLog([string]$msg) {
    $line = "- **[$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))]** $msg"
    Write-Host $line
    Add-Content -Path $logMd -Value $line -Encoding utf8
}

$existingRuns = @(Get-ChildItem (Join-Path $repoRoot 'Training/results') -Directory -Filter '*_unique01' -ErrorAction SilentlyContinue)
if ($existingRuns.Count -lt 16) {
    Write-CampaignLog "Starting Pass 1: Unique01 initial promotion pass (16 fighters, $Pass1Minutes min/wave)."
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'Training/Run-Unique01.ps1') -Minutes $Pass1Minutes -Concurrent $Concurrent -NumEnvs $NumEnvs
    if ($LASTEXITCODE -ne 0) { throw "Pass 1 (Run-Unique01.ps1) failed with exit code $LASTEXITCODE" }
    Write-CampaignLog "Completed Pass 1: All 16 fighters completed initial Unique01 pass and ONNX promotion."
} else {
    Write-CampaignLog "Pass 1 already complete (16/16 *_unique01 runs present and promoted). Entering continuous --resume convergence loop."
}

$cycle = 2
while ((Get-Date) -lt $deadline) {
    $remainingMin = [math]::Round(($deadline - (Get-Date)).TotalMinutes, 1)
    if ($remainingMin -lt 6) {
        Write-CampaignLog "Remaining budget is $remainingMin min (< 6 min threshold). Finishing optimization loop."
        break
    }
    $waveMin = [math]::Min($ResumeWaveMinutes, [math]::Max(5, [int][math]::Floor($remainingMin / 8)))
    Write-CampaignLog "Starting Pass ${cycle}: Continuous convergence resume pass across roster ($waveMin min/wave, $remainingMin min remaining before deadline)."
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'Training/Run-Unique01.ps1') -Minutes $waveMin -Concurrent $Concurrent -NumEnvs $NumEnvs -Resume
    if ($LASTEXITCODE -ne 0) { throw "Pass $cycle (Run-Unique01.ps1 -Resume) failed with exit code $LASTEXITCODE" }
    Write-CampaignLog "Completed Pass ${cycle}."
    $cycle++
}

& $venvPython Tools/generate_training_report.py
Write-CampaignLog "Extended 8-hour optimization loop finished. Final HTML report written to DOCS/20261110/training_status.html and DOCS/training_report.html."
