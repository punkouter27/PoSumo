"""Graduation gate: may this run's brain be exported to the game?

AGENTS.md rule 4: export ONNX only after the target holds for 100 straight
episodes with no NaN gradients. This reads what the run itself recorded and
answers PASS or FAIL, writing GATE.json into the run directory. DeployBrain
refuses a run without a passing GATE.json (unless ungated deploys are switched on
from the PoSumo menu).

What "the target" is. Agent_Biped records, per finished episode:
    Gate/WalkEpisodes, Gate/WalkFailures   - a walker that did not graduate failed
    Gate/SumoEpisodes, Gate/SumoFailures   - a wrestler that did not WIN failed
summed per TensorBoard summary window. The streak is counted from the newest
window backwards: windows with zero failures add their episodes; the first
window with a failure stops the count. Window granularity makes it conservative
- a window holding 99 wins and 1 loss breaks the streak even if the loss was
first.

--school walk is the realistic gate for this project: 100 graduations in a row
without a fall is a real locomotion bar. --school sumo against a self-play pool
that is roughly as strong as the learner is near-impossible by design (it would
mean ~100% wins); it is offered because the rule is literal, and the ELO curve
remains the accept/reject signal for the fight (.claude/rules/training.md).

NaN: every Losses/* and Policy/* scalar must be finite across the whole run, and
every tensor in the newest checkpoint.pt must be finite.

    Training/venv/Scripts/python.exe Tools/graduation_gate.py Training/results/matt_real01 --behavior Matt
"""
import argparse
import glob
import json
import math
import os
import shutil
import sys
from datetime import datetime, timezone

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def scalars(run_dir, behavior):
    from tensorboard.backend.event_processing import event_accumulator

    series = {}
    for path in sorted(glob.glob(os.path.join(run_dir, behavior, "events.out.tfevents.*"))):
        accumulator = event_accumulator.EventAccumulator(path, size_guidance={"scalars": 0})
        accumulator.Reload()
        for tag in accumulator.Tags().get("scalars", []):
            series.setdefault(tag, {})
            for event in accumulator.Scalars(tag):
                series[tag][event.step] = event.value
    return series


def streak(series, school):
    episodes = series.get(f"Gate/{school}Episodes", {})
    failures = series.get(f"Gate/{school}Failures", {})
    total = 0.0
    for step in sorted(episodes, reverse=True):
        if failures.get(step, 0.0) > 0.0:
            break
        total += episodes[step]
    return int(total), len(episodes)


def nan_scalars(series):
    bad = []
    for tag, points in series.items():
        if not (tag.startswith("Losses/") or tag.startswith("Policy/")):
            continue
        if any(not math.isfinite(value) for value in points.values()):
            bad.append(tag)
    return bad


def nan_checkpoint(run_dir, behavior):
    path = os.path.join(run_dir, behavior, "checkpoint.pt")
    if not os.path.exists(path):
        numbered = sorted(glob.glob(os.path.join(run_dir, behavior, f"{behavior}-*.pt")), key=os.path.getmtime)
        if not numbered:
            return None, ["no checkpoint .pt found"]
        path = numbered[-1]
    import torch

    state = torch.load(path, map_location="cpu")
    bad = []

    def walk(prefix, node):
        if torch.is_tensor(node):
            if node.is_floating_point() and not torch.isfinite(node).all():
                bad.append(prefix)
        elif isinstance(node, dict):
            for key, value in node.items():
                walk(f"{prefix}.{key}" if prefix else str(key), value)

    walk("", state)
    return path, bad


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("run_dir")
    parser.add_argument("--behavior", required=True)
    parser.add_argument("--school", choices=["walk", "sumo", "both"], default="walk")
    parser.add_argument("--streak", type=int, default=100)
    args = parser.parse_args()

    run_dir = os.path.abspath(args.run_dir)
    series = scalars(run_dir, args.behavior)
    reasons, streaks = [], {}
    for school in (["Walk", "Sumo"] if args.school == "both" else [args.school.capitalize()]):
        count, windows = streak(series, school)
        streaks[school.lower()] = count
        if windows == 0:
            reasons.append(f"no Gate/{school}Episodes recorded - env built before 2026-11-10?")
        elif count < args.streak:
            reasons.append(f"{school.lower()} streak {count} < {args.streak}")

    nan_tags = nan_scalars(series)
    if nan_tags:
        reasons.append("non-finite scalars: " + ", ".join(nan_tags))
    checkpoint, nan_tensors = nan_checkpoint(run_dir, args.behavior)
    if nan_tensors:
        reasons.append("non-finite checkpoint tensors: " + ", ".join(nan_tensors[:5]))

    passed = not reasons
    run_id = os.path.basename(run_dir.rstrip("\\/"))
    result = {
        "run_id": run_id,
        "behavior": args.behavior,
        "school": args.school,
        "required_streak": args.streak,
        "streaks": streaks,
        "checkpoint": checkpoint and os.path.relpath(checkpoint, ROOT).replace("\\", "/"),
        "passed": passed,
        "reasons": reasons,
        "checked": datetime.now(timezone.utc).isoformat(timespec="seconds"),
    }
    with open(os.path.join(run_dir, "GATE.json"), "w", encoding="utf-8") as handle:
        json.dump(result, handle, indent=2)
    # Carry the preflight stamp into the run so the versions travel with the weights.
    stamp = os.path.join(ROOT, "Training", "stamps", f"{run_id}.json")
    if os.path.exists(stamp):
        shutil.copyfile(stamp, os.path.join(run_dir, "STAMP.json"))

    print(json.dumps(result, indent=2))
    print("GATE RESULT:", "PASS" if passed else "FAIL - " + "; ".join(reasons))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
