<#
.SYNOPSIS
    Unattended 8-hour multi-agent RL optimization loop for PoSumo.
    Prioritizes urgent Unique01 promotion across all 16 fighters, then runs
    continuous round-robin resume waves to maximize ELO and reward convergence,
    exporting production ONNX weights and updating the HTML report after every wave.
#>
param(
    [double]$TotalHours = 8.0,
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

@"
# 2026-11-10 — Extended 8-Hour Multi-Agent RL Optimization Loop

- **Started:** $($startTime.ToString('yyyy-MM-dd HH:mm:ss'))
- **Target Completion:** $($deadline.ToString('yyyy-MM-dd HH:mm:ss')) ($TotalHours hours)
- **Estimated Timeline:**
  - **Build Step (~12 min):** Batch-build `GrandmaEnv`, `KimEnv`, `MattEnv`, `NickEnv` from latest C# scripts.
  - **Pass 1 — Urgent Unique01 Promotion (~110 min):** 8 waves × 2 concurrent fighters × $Pass1Minutes min each. Promotes all 12 placeholder fighters (`Alan`, `Dupee`, `Porto`, `Tong`, `Brendan`, `Maya`, `Velez`, `Baily`, `Josh`, `Decoy`, `Manzi`, `Sokol`) from borrowed brains to dedicated policies and adapts all 16 fighters to the non-overlapping shikiri crouch (`hip -90° / knee 90° / spine1 20°`).
  - **Pass 2+ — Continuous Convergence Loop (~360 min):** Round-robin `--resume` waves ($ResumeWaveMinutes min per wave) across all 16 fighters until the 8-hour wall-clock budget is reached, continuously checkpointing, deploying `.onnx` weights to `Assets/Agents/<Name>_v01/<Name>.onnx`, and updating `DOCS/20261110/training_status.html` and `DOCS/training_report.html`.

## Progress Log
"@ | Set-Content -Path $logMd -Encoding utf8

Write-CampaignLog "Starting Pass 1: Unique01 initial promotion pass (16 fighters, $Pass1Minutes min/wave, -Rebuild)."
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'Training/Run-Unique01.ps1') -Minutes $Pass1Minutes -Concurrent $Concurrent -NumEnvs $NumEnvs
if ($LASTEXITCODE -ne 0) { throw "Pass 1 (Run-Unique01.ps1) failed with exit code $LASTEXITCODE" }
Write-CampaignLog "Completed Pass 1: All 16 fighters completed initial Unique01 pass and ONNX promotion."

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
