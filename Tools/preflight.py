"""Preflight a training config BEFORE the trainer starts, and stamp the run.

Why: every expensive mistake in this project's training history was a silent
mismatch between things that must agree - an observation vector one slot wider
than the brain, an env built before the code it was meant to train, a mat 0.5 m
wider in training than in the game. None of them raised an error; each cost a
run. This checks the agreements that can be checked from disk, refuses to launch
on a hard mismatch, and writes a stamp naming the INTERFACE and PHYSICS versions
the run trains against (AGENTS.md rule 3: tag each checkpoint with both).

Checks, per behavior in the config:
  * a character sheet exists for it;
  * the observation vector it will build, from the sheet flags plus any
    -posumoBalanceObs in env_settings.env_args, and the block sizes read out of
    Agent_Biped.cs (not assumed);
  * the action count from Agent_Biped.cs;
  * the deployed .onnx input width against that vector - a mismatch is fine for a
    cold run or a widened warm start, so it is reported, and an ERROR only when
    --initialize-from is given without --widened;
  * the env build exists and is NEWER than every script under Assets/Scripts
    (an env older than the code trains the old code);
and project-wide:
  * fixed timestep 0.02 s, gravity -9.81, solver iterations 16 / 14.

Exit 0 = launch, 1 = refuse. The stamp goes to Training/stamps/<run_id>.json.

    python Tools/preflight.py Training/configs/MattReal01.yaml --run-id matt_real01
"""
import argparse
import glob
import hashlib
import json
import os
import re
import sys
from datetime import datetime, timezone

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AGENT_CS = os.path.join(ROOT, "Assets", "Scripts", "Agent", "Agent_Biped.cs")
BODY_CS = os.path.join(ROOT, "Assets", "Scripts", "Agent", "Agent_BipedBody.cs")
TUNING = os.path.join(ROOT, "Assets", "Settings", "GameTuning.asset")
TIME = os.path.join(ROOT, "ProjectSettings", "TimeManager.asset")
PHYS2D = os.path.join(ROOT, "ProjectSettings", "Physics2DSettings.asset")
STAMPS = os.path.join(ROOT, "Training", "stamps")
BALANCE_ARG = "-posumoBalanceObs"

# Sheet field -> the flag name used in Agent_Biped.ResolvedObservationCount.
FLAGS = ["contactObservations", "staminaObservation", "extendedObservations",
         "intentObservations", "balanceObservations"]


def read(path):
    with open(path, encoding="utf-8") as handle:
        return handle.read()


def sha(*parts):
    digest = hashlib.sha1()
    for part in parts:
        digest.update(part.encode("utf-8"))
    return digest.hexdigest()[:12]


def load_yaml(path):
    try:
        import yaml
    except ImportError:
        sys.exit("preflight: PyYAML missing - run with Training/venv/Scripts/python.exe")
    with open(path, encoding="utf-8") as handle:
        return yaml.safe_load(handle)


def code_contract():
    """Observation base, block sizes and action count, read from the source."""
    src = read(AGENT_CS)
    base = int(re.search(r"public const int ObservationCount = (\d+);", src).group(1))
    motors = int(re.search(r"public const int MotorCount = (\d+);", src).group(1))
    extra = int(re.search(r"public const int ActionCount = MotorCount \+ (\d+);", src).group(1))
    resolved = re.search(r"ResolvedObservationCount =>(.*?);", src, re.S).group(1)
    blocks = {name: int(size) for name, size in re.findall(r"(\w+) \? (\d+) : 0", resolved)}
    missing = [flag for flag in FLAGS if flag not in blocks]
    if missing:
        sys.exit(f"preflight: Agent_Biped.ResolvedObservationCount no longer names {missing}; update FLAGS")
    return base, blocks, motors + extra


def sheet(behavior):
    path = os.path.join(ROOT, "Assets", "Agents", f"{behavior}_v01", f"{behavior}_Character.asset")
    if not os.path.exists(path):
        return None, path
    text = read(path)
    values = {}
    for flag in FLAGS + ["decisionPeriod", "borrowedBrainFrom"]:
        match = re.search(rf"^\s*{flag}:\s*(.*)$", text, re.M)
        values[flag] = match.group(1).strip() if match else None
    return values, path


def onnx_width(path):
    try:
        import onnx
    except ImportError:
        return None
    if not os.path.exists(path):
        return None
    model = onnx.load(path)
    for tensor in model.graph.input:
        if tensor.name.startswith("obs_0") or tensor.name == "vector_observation":
            dims = tensor.type.tensor_type.shape.dim
            return dims[-1].dim_value or None
    return None


def physics_problems():
    problems = []
    time_text = read(TIME)
    count = re.search(r"m_Count:\s*(\d+)", time_text)
    numerator = re.search(r"m_Numerator:\s*(\d+)", time_text)
    if count and numerator:
        step = int(count.group(1)) / int(numerator.group(1))
        if abs(step - 0.02) > 1e-6:
            problems.append(f"fixed timestep is {step:.5f} s, every brain was fitted at 0.02")
    phys = read(PHYS2D)
    gravity = re.search(r"m_Gravity: \{x: ([-\d.]+), y: ([-\d.]+)\}", phys)
    if gravity and abs(float(gravity.group(2)) + 9.81) > 1e-3:
        problems.append(f"gravity y is {gravity.group(2)}, not -9.81")
    for key, want in (("m_VelocityIterations", 14), ("m_PositionIterations", 16)):
        match = re.search(rf"{key}: (\d+)", phys)
        if match and int(match.group(1)) != want:
            problems.append(f"{key} is {match.group(1)}, every brain was fitted at {want}")
    return problems


def physics_version():
    body = read(BODY_CS)
    tables = re.search(r"PART_DEFS =.*?JOINT_DEFS =.*?};", body, re.S)
    consts = "".join(re.findall(r"private const float [A-Z_]+ = [^;]+;", body))
    return sha(tables.group(0) if tables else body, consts, read(TIME), read(PHYS2D), read(TUNING))


def newest_script_mtime():
    files = glob.glob(os.path.join(ROOT, "Assets", "Scripts", "**", "*.cs"), recursive=True)
    return max(os.path.getmtime(path) for path in files)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("config")
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--initialize-from", default="")
    parser.add_argument("--widened", action="store_true",
                        help="the warm-start trunk was widened with Tools/onnx_warmstart.py --new-obs")
    parser.add_argument("--env", default="", help="env exe; default Builds/<Name>Env/<Name>Env.exe")
    parser.add_argument("--no-stamp", action="store_true")
    args = parser.parse_args()

    config = load_yaml(args.config)
    env_args = ((config.get("env_settings") or {}).get("env_args")) or []
    force_balance = BALANCE_ARG in env_args
    base, blocks, actions = code_contract()

    errors, notes, behaviors = [], [], {}
    for behavior in (config.get("behaviors") or {}):
        values, path = sheet(behavior)
        if values is None:
            errors.append(f"{behavior}: no character sheet at {os.path.relpath(path, ROOT)}")
            continue
        flags = {flag: values.get(flag) == "1" for flag in FLAGS}
        if force_balance:
            flags["balanceObservations"] = True
        width = base + sum(blocks[flag] for flag, on in flags.items() if on)
        deployed = os.path.join(os.path.dirname(path), f"{behavior}.onnx")
        model_width = onnx_width(deployed)
        if model_width is not None and model_width != width:
            message = (f"{behavior}: deployed {behavior}.onnx takes {model_width} inputs, this run "
                       f"builds {width}")
            if args.initialize_from and not args.widened:
                errors.append(message + " - warm start needs Tools/onnx_warmstart.py --new-obs "
                              f"{width - model_width}, then pass --widened")
            else:
                notes.append(message + " (fine for a cold or widened run; deploy sets the sheet flags)")

        exe = args.env or os.path.join(ROOT, "Builds", f"{behavior}Env", f"{behavior}Env.exe")
        if not os.path.exists(exe):
            errors.append(f"{behavior}: no env build at {os.path.relpath(exe, ROOT)} - PoSumo > Build {behavior} Training Env")
        elif os.path.getmtime(exe) < newest_script_mtime():
            errors.append(f"{behavior}: env build is OLDER than Assets/Scripts - it would train the old code. Rebuild it.")

        behaviors[behavior] = {
            "observations": width,
            "actions": actions,
            "flags": flags,
            "decision_period": values.get("decisionPeriod"),
            "deployed_onnx_inputs": model_width,
        }

    errors += physics_problems()
    interface = sha(json.dumps({name: [b["observations"], b["actions"], sorted(k for k, v in b["flags"].items() if v),
                                       b["decision_period"]] for name, b in behaviors.items()}, sort_keys=True))
    physics = physics_version()

    for note in notes:
        print("preflight NOTE :", note)
    for error in errors:
        print("preflight ERROR:", error)
    for name, info in behaviors.items():
        print(f"preflight OK   : {name} {info['observations']} obs / {info['actions']} actions")
    print(f"preflight STAMP: interface {interface} physics {physics}")

    if not args.no_stamp and not errors:
        os.makedirs(STAMPS, exist_ok=True)
        stamp = {
            "run_id": args.run_id,
            "config": os.path.relpath(os.path.abspath(args.config), ROOT).replace("\\", "/"),
            "initialize_from": args.initialize_from or None,
            "interface_version": interface,
            "physics_version": physics,
            "behaviors": behaviors,
            "written": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        }
        with open(os.path.join(STAMPS, f"{args.run_id}.json"), "w", encoding="utf-8") as handle:
            json.dump(stamp, handle, indent=2)
    print("PREFLIGHT RESULT:", "FAIL" if errors else "PASS")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
