# Opens the training Benchmark page (benchmark/page.html) for this repo: one button runs ~10 s of real mlagents-learn
# training (MattUnique01, 2 headless env players) and records step time, machine and machine load in
# benchmark/history.json (commit it to compare laptops). The first run on a machine builds Builds/MattEnv in Unity
# batch mode, so the Unity Editor must be closed and Unity Hub signed in.
# -Run: no page; runs one Benchmark in this console, records it in benchmark/history.json and exits 0 (recorded) or 1.
$ErrorActionPreference = 'Stop'
$py = Join-Path $PSScriptRoot 'Training/venv/Scripts/python.exe'
if (-not (Test-Path $py)) {
    Write-Host 'Creating Training/venv (Python 3.10.11, pinned by Training/constraints.txt)...'
    Push-Location $PSScriptRoot
    try {
        uv venv --python 3.10.11 Training/venv
        uv pip install --python $py -c Training/constraints.txt 'torch==2.5.1' --index-url https://download.pytorch.org/whl/cpu
        uv pip install --python $py -c Training/constraints.txt -e Training/ml-agents/ml-agents-envs -e Training/ml-agents/ml-agents
    } finally { Pop-Location }
}
& $py (Join-Path $PSScriptRoot 'benchmark/serve.py') @args
exit $LASTEXITCODE
