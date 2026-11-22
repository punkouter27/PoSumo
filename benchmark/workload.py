"""PoSumo Workload: mlagents-learn on the Unique01 recipe (Matt), 2 headless env players, as Run-Unique01.ps1 runs it.

Run by serve.py with the training venv:  Training/venv/Scripts/python.exe benchmark/workload.py
Benchmark player: Builds/MattEnv/MattEnv.exe, built here in Unity batch mode on first use (Unity Editor closed).

Hooks: TrainerController.advance = one env-manager step (sim) followed by trainer.advance (learn: trajectory
processing and, once per full buffer, the PPO update). Warm-up ends after the first update; the window then ends
on a later update so it always holds whole collect-then-update cycles. A Step = every env player advancing once.

Deliberately NO TensorBoard (an exception to .claude/rules/training.md, which exists so real runs can be judged on
ELO): this is a ~10 s timing run that is never judged or deployed, and a TensorBoard process would add load that
skews the number. Its run dir Training/results/_benchmark is throwaway (--force; gitignored).
"""

from __future__ import annotations

import datetime
import os
import subprocess
import sys
import time
from importlib import metadata
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import core  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
FIGHTER, ENV = "Matt", "MattEnv"
CONFIG = ROOT / "Training" / "configs" / f"{FIGHTER}Unique01.yaml"
PLAYER = ROOT / "Builds" / ENV / f"{ENV}.exe"
BUILD_METHOD = f"PoSumo.EditorTools.BuildTrainingEnv.Build{FIGHTER}"
NUM_ENVS = 2  # Run-Unique01.ps1 default
RUN_ID = "_benchmark"


def version(pkg: str) -> str | None:
    try:
        return metadata.version(pkg)
    except metadata.PackageNotFoundError:
        return None


def unity_version() -> str:
    text = (ROOT / "ProjectSettings" / "ProjectVersion.txt").read_text()
    return text.split("m_EditorVersion:")[1].split()[0]


def build_player() -> None:
    """Build the Benchmark player in Unity batch mode (first use only; needs the Unity Editor closed)."""
    unity = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Unity/Hub/Editor" / unity_version()
    unity = unity / "Editor/Unity.exe"
    if not unity.exists():
        raise RuntimeError(f"Unity {unity_version()} is not installed (needed once to build {PLAYER.name})")
    core.say(f"building the Benchmark player {ENV} (first run on this machine, several minutes)")
    log = ROOT / "Logs" / "benchmark_build.log"
    log.parent.mkdir(exist_ok=True)
    args = ["-batchmode", "-nographics", "-quit", "-projectPath", str(ROOT), "-executeMethod", BUILD_METHOD]
    subprocess.run([str(unity), *args, "-logFile", str(log)], check=False)
    text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    if not PLAYER.exists() or "BUILD RESULT: Succeeded" not in text:
        hint = "see Logs/benchmark_build.log"
        if "No valid Unity Editor license" in text:
            hint = "Unity licence not active - sign in to Unity Hub, then run the Benchmark again"
        elif "another Unity instance" in text:
            hint = "close the Unity Editor (it holds the project lock) and retry"
        raise RuntimeError(f"Benchmark player build failed: {hint}")


def workload() -> dict:
    import yaml

    b = yaml.safe_load(CONFIG.read_text())["behaviors"][FIGHTER]
    built = datetime.datetime.fromtimestamp(PLAYER.stat().st_mtime).isoformat(timespec="seconds")
    return dict(
        repo="PoSumo",
        trainer=f"mlagents {version('mlagents')} (torch {version('torch')})",
        config=f"Training/configs/{CONFIG.name}",
        config_hash=core.workload_id(b),
        num_envs=NUM_ENVS,
        env_args=f"-posumoFighter {FIGHTER}",
        hyperparameters=b["hyperparameters"],
        net=[b["network_settings"]["hidden_units"]] * b["network_settings"]["num_layers"],
        time_horizon=b.get("time_horizon"),
        unity=unity_version(),
        player_built=built,
    )


def main() -> None:
    from mlagents.trainers import learn
    from mlagents.trainers.trainer.on_policy_trainer import OnPolicyTrainer
    from mlagents.trainers.trainer.rl_trainer import RLTrainer
    from mlagents.trainers.trainer_controller import TrainerController

    meter = None
    try:
        if not PLAYER.exists():
            build_player()
        core.T_PROCESS = time.time()  # Startup time excludes the one-time player build
        versions = dict(python=sys.version.split()[0], torch=version("torch"), mlagents=version("mlagents"))
        meter = core.Meter(workload(), versions, warmup_blocks=None)
    except Exception as e:  # noqa: BLE001 - every failure becomes a failed row
        meter = meter or core.Meter({"repo": "PoSumo", "config": CONFIG.name}, {"python": sys.version.split()[0]})
        meter.finish(ok=False, error=core.fail_text(e))

    state = {"learn": 0.0, "updated": False}
    trainer_advance, update_policy, tc_advance = RLTrainer.advance, OnPolicyTrainer._update_policy, None

    def trainer_advance_(self):
        t = time.perf_counter()
        trainer_advance(self)
        state["learn"] += time.perf_counter() - t

    def update_policy_(self):
        done = update_policy(self)
        state["updated"] = True
        meter.end_warmup()  # no-op once measuring; the call holding this update is still warm-up (see below)
        return done

    tc_advance = TrainerController.advance

    def tc_advance_(self, env_manager):
        if not meter.on_exit:
            meter.on_exit.append(env_manager.close)  # shut the Unity players down before the process ends
        was_measuring = meter.measuring
        state["learn"], state["updated"] = 0.0, False
        t = time.perf_counter()
        n = tc_advance(self, env_manager)
        total = time.perf_counter() - t
        if was_measuring:
            meter.block(total - state["learn"], state["learn"], n / NUM_ENVS)
        return n

    meter.can_finish = lambda: state["updated"]
    RLTrainer.advance, OnPolicyTrainer._update_policy, TrainerController.advance = (
        trainer_advance_,
        update_policy_,
        tc_advance_,
    )
    sys.argv = [
        "mlagents-learn",
        str(CONFIG),
        f"--run-id={RUN_ID}",
        f"--results-dir={ROOT / 'Training' / 'results'}",
        f"--env={PLAYER}",
        f"--num-envs={NUM_ENVS}",
        "--no-graphics",
        "--base-port=5705",
        "--force",
        "--env-args",
        "-posumoFighter",
        FIGHTER,
    ]
    os.chdir(ROOT)
    try:
        learn.main()
        meter.finish(ok=False, error="training ended before the measured window finished")
    except BaseException as e:  # noqa: BLE001
        meter.finish(ok=False, error=core.fail_text(e))


if __name__ == "__main__":
    main()
