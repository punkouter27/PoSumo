<#
.SYNOPSIS
    Unique01 campaign: train all 16 fighters, each with its own recipe, for
    $Minutes minutes each, $Concurrent at a time. Created 2026-11-10.

.DESCRIPTION
    Every fighter trains under its OWN behavior name and physique, in its donor's
    training scene, with `--env-args -posumoFighter <Name>` (Systems_TrainingFighter).
    Recipes: Training/configs/<Name>Unique01.yaml, written by
    Tools/make_unique01_configs.py. Donors: Training/unique01_roster.csv.

    Order of work, all with the Unity Editor CLOSED (AGENTS.md §4: close it for runs
    over 30 min; the env builds below also need the project lock):
      1. build the four donor envs in batch mode (skipped if present, -Rebuild forces)
      2. prune event-less *_intentinit staging runs out of the TensorBoard logdir
      3. stage each warm start: <donor>_intent01 checkpoint -> <name>_uniqueinit/<Name>
      4. TensorBoard on 6006 (started here unless already serving Training/results)
      5. waves of $Concurrent trainers; each gets $Minutes of wall clock from the
         moment its env players exist, then a graceful close (final checkpoint + ONNX)

    Status after every run: DOCS/20261110/unique01_status.json. Full trainer output:
    Training/results/<run>/trainer.log is NOT captured (the trainer needs its own
    console window for the graceful close) — the run's own run_logs/ hold the record.

.EXAMPLE
    pwsh -File Training/Run-Unique01.ps1
    pwsh -File Training/Run-Unique01.ps1 -Fighters Matt,Tong -Minutes 5
#>
param(
    [string[]]$Fighters,
    [ValidateRange(1, 120)][int]$Minutes = 5,
    [ValidateRange(1, 4)][int]$Concurrent = 2,
    [ValidateRange(1, 4)][int]$NumEnvs = 2,
    [int]$BasePort = 5105,
    [int]$TensorBoardPort = 6006,
    [switch]$Rebuild,
    [switch]$Resume
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
$learnExe = Join-Path $repoRoot 'Training/venv/Scripts/mlagents-learn.exe'
$venvPython = Join-Path $repoRoot 'Training/venv/Scripts/python.exe'
$unityExe = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
$statusFile = Join-Path $repoRoot 'DOCS/20261110/unique01_status.json'

$roster = Import-Csv (Join-Path $repoRoot 'Training/unique01_roster.csv')
if ($Fighters) { $roster = $roster | Where-Object { $Fighters -contains $_.fighter } }
if (-not $roster) { throw 'no fighters selected' }

# --- 0. the Editor must be closed --------------------------------------------
$editor = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
    Where-Object { $_.CommandLine -match [regex]::Escape('PoSumo') -or -not $_.CommandLine }
if ($editor) {
    throw "Unity is still running (PID $($editor.ProcessId -join ', ')). Close the Editor first: the env builds need the project, and AGENTS.md §4 forbids an open Editor during a campaign this long."
}

$waves = [math]::Ceiling($roster.Count / $Concurrent)
$estimate = $waves * ($Minutes + 2)
Write-Host "Unique01: $($roster.Count) fighters, $Concurrent at a time, $Minutes min each."
Write-Host "  ESTIMATE ~$estimate min of training waves (+ ~$(4 * 3) min if the four envs must be built)."

# --- 1. envs -------------------------------------------------------------------
$donors = $roster.donor | Sort-Object -Unique
foreach ($donor in $donors) {
    $exe = Join-Path $repoRoot "Builds/${donor}Env/${donor}Env.exe"
    if ((Test-Path $exe) -and -not $Rebuild) { Write-Host "  env ok   $donor"; continue }
    $log = Join-Path $repoRoot "Logs/build_${donor}Env.log"
    Write-Host "  building ${donor}Env (batch mode) ..."
    $p = Start-Process -FilePath $unityExe -PassThru -Wait -NoNewWindow -ArgumentList @(
        '-batchmode', '-quit', '-projectPath', "`"$repoRoot`"",
        '-executeMethod', "PoSumo.EditorTools.BuildTrainingEnv.Build$donor",
        '-logFile', "`"$log`"")
    $result = Select-String -Path $log -Pattern 'BUILD RESULT:.*' | Select-Object -Last 1
    if (-not (Test-Path $exe) -or ($result -and $result.Line -notmatch 'Succeeded')) {
        throw "${donor}Env build failed (exit $($p.ExitCode)): $($result.Line) — see $log"
    }
    Write-Host "  built    $donor — $($result.Line)"
}

# --- 2. prune obsolete staging runs (no events, superseded by *_intent01) -----------
Get-ChildItem Training/results -Directory -Filter '*_intentinit' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  prune    $($_.Name)"
    Remove-Item -LiteralPath $_.FullName -Recurse -Force
}

# --- 3. stage warm starts + preflight stamps ------------------------------------
foreach ($row in $roster) {
    $name = $row.fighter; $donor = $row.donor
    $run = "$($name.ToLowerInvariant())_unique01"
    if (Test-Path "Training/results/$run") {
        if ($Resume) {
            Write-Host "  resume   $name ($run already exists)"
            continue
        }
        throw "Training/results/$run already exists — delete it or pass -Resume."
    }
    $sheetPath = "Assets/Agents/${name}_v01/${name}_Character.asset"
    $usesBalance = (Test-Path $sheetPath) -and (Select-String -Path $sheetPath -Pattern '^\s*balanceObservations:\s*1\b' -Quiet)
    $phase = if ($usesBalance -and (Test-Path "Training/results/$($donor.ToLowerInvariant())_real01/$donor/checkpoint.pt")) { 'real01' } else { 'intent01' }
    $src = "Training/results/$($donor.ToLowerInvariant())_${phase}/$donor/checkpoint.pt"
    if (-not (Test-Path $src)) { throw "warm-start source missing: $src" }
    $dst = "Training/results/$($name.ToLowerInvariant())_uniqueinit/$name"
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Copy-Item -LiteralPath $src -Destination "$dst/checkpoint.pt" -Force
    & $venvPython Tools/preflight.py "Training/configs/${name}Unique01.yaml" --run-id $run --initialize-from "$($name.ToLowerInvariant())_uniqueinit" --env "Builds/${donor}Env/${donor}Env.exe"
    if ($LASTEXITCODE -ne 0) { throw "preflight failed for $name ($run)" }
}

# --- 4. TensorBoard ---------------------------------------------------------------
$tbOwner = Get-NetTCPConnection -State Listen -LocalPort $TensorBoardPort -ErrorAction SilentlyContinue |
    Select-Object -First 1 | ForEach-Object { (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.OwningProcess)").CommandLine }
if ($tbOwner) {
    if ($tbOwner -notmatch 'Training[/\\]results') { throw "port $TensorBoardPort is held by another logdir: $tbOwner" }
    Write-Host "  TensorBoard already serving Training/results on $TensorBoardPort"
} else {
    Start-Process -FilePath $venvPython -WindowStyle Minimized -WorkingDirectory $repoRoot -ArgumentList @(
        '-m', 'tensorboard.main', '--logdir', 'Training/results', '--port', "$TensorBoardPort", '--reload_interval', '15') | Out-Null
    Write-Host "  TensorBoard  http://localhost:$TensorBoardPort"
}

# --- 5. waves -----------------------------------------------------------------------
$status = [ordered]@{ campaign = 'unique01'; startedLocal = (Get-Date).ToString('s'); minutesEach = $Minutes; runs = @() }
function Save-Status { $status | ConvertTo-Json -Depth 5 | Set-Content -Path $statusFile -Encoding utf8 }

function EnvPlayers([string]$name) {
    @(Get-CimInstance Win32_Process -Filter "Name LIKE '%Env.exe'" |
        Where-Object { $_.CommandLine -match "posumoFighter\s+$name(\s|$)" })
}

for ($wave = 0; $wave -lt $waves; $wave++) {
    $batch = @($roster | Select-Object -Skip ($wave * $Concurrent) -First $Concurrent)
    $live = @()
    $port = $BasePort
    foreach ($row in $batch) {
        $name = $row.fighter; $donor = $row.donor
        $run = "$($name.ToLowerInvariant())_unique01"
        $initFlag = if ($Resume -and (Test-Path "Training/results/$run/$name/checkpoint.pt")) { '--resume' } else { "--initialize-from=$($name.ToLowerInvariant())_uniqueinit" }
        $learnArgs = @(
            "Training/configs/${name}Unique01.yaml",
            "--run-id=$run",
            '--results-dir=Training/results',
            "--env=Builds/${donor}Env/${donor}Env.exe",
            "--num-envs=$NumEnvs",
            "--base-port=$port",
            '--no-graphics',
            $initFlag,
            '--env-args', '-posumoFighter', $name)   # --env-args must be LAST
        $proc = Start-Process -FilePath $learnExe -PassThru -WorkingDirectory $repoRoot -ArgumentList $learnArgs
        Write-Host ("[{0:HH:mm:ss}] wave {1}/{2}: {3} ({4}, env {5}) trainer {6} ports {7}+" -f (Get-Date), ($wave + 1), $waves, $name, $row.style, $donor, $proc.Id, $port)
        $live += [pscustomobject]@{ name = $name; run = $run; style = $row.style; donor = $donor; proc = $proc; clock = $null; launched = Get-Date }
        $port += 10
    }

    # Each trainer's clock starts when ITS env players exist, not at launch.
    while ($true) {
        $pending = $false
        foreach ($r in $live) {
            if ($r.proc.HasExited) { continue }
            if (-not $r.clock) {
                if ((EnvPlayers $r.name).Count -ge 1) { $r.clock = Get-Date; Write-Host ("[{0:HH:mm:ss}]   {1}: envs up, {2} min clock started" -f (Get-Date), $r.name, $Minutes) }
                elseif (((Get-Date) - $r.launched).TotalMinutes -gt 4) { Write-Warning "$($r.name): no env player after 4 min"; $r.clock = Get-Date }
                $pending = $true; continue
            }
            if (((Get-Date) - $r.clock).TotalMinutes -lt $Minutes) { $pending = $true; continue }
            Write-Host ("[{0:HH:mm:ss}]   {1}: time — closing trainer for a final checkpoint" -f (Get-Date), $r.name)
            if (-not $r.proc.CloseMainWindow()) { Write-Warning "$($r.name): no window to close" }
        }
        if (-not $pending) { break }
        Start-Sleep -Seconds 10
    }

    foreach ($r in $live) {
        if (-not $r.proc.WaitForExit(120000)) { Write-Warning "$($r.name): trainer did not exit in 120 s — killing"; $r.proc.Kill() }
        EnvPlayers $r.name | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        $dir = "Training/results/$($r.run)/$($r.name)"
        $finalOnnx = Test-Path "Training/results/$($r.run)/$($r.name).onnx"
        $numbered = @(Get-ChildItem $dir -Filter "$($r.name)-*.onnx" -ErrorAction SilentlyContinue | Sort-Object { [int]($_.BaseName -replace '.*-', '') })
        $steps = if ($numbered) { [int]($numbered[-1].BaseName -replace '.*-', '') } else { 0 }
        $entry = [ordered]@{ fighter = $r.name; style = $r.style; donor = $r.donor; run = $r.run
            finalOnnx = $finalOnnx; lastNumberedOnnx = if ($numbered) { $numbered[-1].Name } else { $null }; steps = $steps
            minutes = if ($r.clock) { [math]::Round(((Get-Date) - $r.clock).TotalMinutes, 1) } else { 0 } }
        $status.runs += $entry
        Save-Status
        # Deploy production ONNX weights in-place (preserving .meta GUID) and promote placeholder brain ownership
        $bestOnnx = if ($finalOnnx) { "Training/results/$($r.run)/$($r.name).onnx" } elseif ($numbered) { $numbered[-1].FullName } else { $null }
        if ($bestOnnx -and (Test-Path $bestOnnx)) {
            $dstOnnx = "Assets/Agents/$($r.name)_v01/$($r.name).onnx"
            Copy-Item -LiteralPath $bestOnnx -Destination $dstOnnx -Force
            $charAsset = "Assets/Agents/$($r.name)_v01/$($r.name)_Character.asset"
            if (Test-Path $charAsset) {
                $raw = Get-Content -LiteralPath $charAsset -Raw
                $updated = $raw -replace '(?m)^(\s*borrowedBrainFrom:)\s*\S+', '$1 '
                if ($updated -ne $raw) { Set-Content -LiteralPath $charAsset -Value $updated -NoNewline -Encoding utf8 }
            }
            $stampSrc = "Training/stamps/$($r.run).json"
            if (Test-Path $stampSrc) { Copy-Item -LiteralPath $stampSrc -Destination "Training/results/$($r.run)/STAMP.json" -Force }
            Write-Host ("[{0:HH:mm:ss}]   {1}: promoted ONNX -> {2}" -f (Get-Date), $r.name, $dstOnnx)
        }
        Write-Host ("[{0:HH:mm:ss}]   {1}: done — final onnx {2}, last numbered {3}" -f (Get-Date), $r.name, $finalOnnx, $entry.lastNumberedOnnx)
    }
    # The staging dirs hold weights, not history: out of the logdir once used.
    foreach ($r in $live) { Remove-Item -Recurse -Force "Training/results/$($r.name.ToLowerInvariant())_uniqueinit" -ErrorAction SilentlyContinue }
    & $venvPython Tools/generate_training_report.py
}

$status.finishedLocal = (Get-Date).ToString('s')
Save-Status
Write-Host "UNIQUE01 RESULT: $(@($status.runs | Where-Object { $_.finalOnnx -or $_.lastNumberedOnnx }).Count)/$($roster.Count) fighters produced an ONNX. Status: $statusFile"
Write-Host 'Next: reopen Unity, then deploy each run (Tools/deploy_unique01 step in DOCS/20261110/unique01.md).'
