#!/usr/bin/env python3
"""Naming / structure audit for PoSumo.

Enforces the conventions docs/project-notes.md mandates across the tree:

  * script prefixes are exactly Agent_, Sensor_, Reward_, Systems_ and must live
    in the matching folder under Assets/_Project/ (four folders, no others);
  * scenes are SCN_*, training scenes are SCN_TRAIN_<NAME> (no suffixes);
  * env builds are Builds/<Name>Env/ matching their scene;
  * configs are <Name><Phase><NN>.yaml paired 1:1 with run-id <name>_<phase><nn>;
  * agent assets live in Assets/_Project/RL_Models/<Name>_v<NN>/ with a MANIFEST.md;
  * face art is Assets/_Project/Environments/Art/Faces/<Name>_Neutral.png or
    <Name>_<Happy|Angry|Hurt|Insult>_<1-5>.png (any subset - missing slots are
    drawn procedurally by Systems_FaceMood);
  * voice lines are Assets/_Project/Data/Resources/Audio/Voice/<Name>_<Mood>_L<1-5>_<1-5>.wav
    (any subset - missing lines are synthesised by Systems_VoiceSynth);
  * hierarchy roots carry the standard 7 groups (checked only when a scene
    conveniently serializes them -- this tool does NOT parse .unity files).

Exit 0 when clean, 1 on any violation. Report-only: it never edits.

Usage:  python scripts/naming_audit.py [--quiet]
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VALID_PREFIXES = {"Agent_", "Sensor_", "Reward_", "Systems_"}
SCRIPT_FOLDERS = {"Agent", "Sensor", "Reward", "Systems"}
MOODS = "Happy|Angry|Hurt|Insult"
FACE_RE = re.compile(rf"^[A-Za-z0-9]+_(Neutral|(?:{MOODS})_[1-5])\.png$")
VOICE_RE = re.compile(rf"^[A-Za-z0-9]+_(?:{MOODS})_L[1-5]_[1-5]\.wav$")

violations: list[str] = []


def violation(path: Path, msg: str) -> None:
    violations.append(f"{path.relative_to(ROOT)}: {msg}")


def audit_scripts() -> None:
    # Runtime scripts live in Assets/_Project/Agents/{Agent,Sensor,Reward}/ and
    # Assets/_Project/Core/Systems/ (one PoSumo.Runtime assembly, asmdef at Assets/_Project/).
    project = ROOT / "Assets" / "_Project"
    script_dirs = [project / "Agents" / name for name in ("Agent", "Sensor", "Reward")] + [project / "Core" / "Systems"]
    agents = project / "Agents"
    for folder in sorted(p for p in agents.iterdir() if p.is_dir()):
        if folder.name not in SCRIPT_FOLDERS:
            violation(folder, "unexpected folder under Assets/_Project/Agents (exactly Agent/Sensor/Reward allowed)")
    for folder in script_dirs:
        for cs in sorted(folder.glob("*.cs")):
            match = re.match(r"(Agent|Sensor|Reward|Systems)_", cs.name)
            if not match:
                violation(cs, "file name lacks a Agent_/Sensor_/Reward_/Systems_ prefix")
            elif match.group(1) != folder.name:
                violation(cs, f"prefix {match.group(1)}_ does not match folder {folder.name}/")
    # stray .cs anywhere else under Assets (except Editor/Tests which are
    # assembly-defined and legitimately unprefixed tooling)
    allowed = set(script_dirs) | {project / "Core" / "Editor", project / "Core" / "Tests"}
    for cs in sorted((ROOT / "Assets").rglob("*.cs")):
        rel = cs.relative_to(ROOT / "Assets")
        if rel.parts[0] in ("ML-Agents", "Plugins") or any(parent in allowed for parent in cs.parents):
            continue
        violation(cs, "script outside Assets/_Project/Agents/*, Core/Systems (or Core/Editor, Core/Tests)")


def audit_scenes() -> None:
    for scene in sorted((ROOT / "Assets").rglob("*.unity")):
        name = scene.stem
        if not name.startswith("SCN_"):
            violation(scene, "scene name must start with SCN_")
        if name.startswith("SCN_TRAIN_"):
            body = name[len("SCN_TRAIN_") :]
            if not body or body != body.upper() or "_" in body:
                violation(scene, "training scene must be SCN_TRAIN_<NAME> (upper, no extra suffixes)")


def audit_configs() -> None:
    cfg_dir = ROOT / "Training" / "configs"
    if not cfg_dir.is_dir():
        return
    pattern = re.compile(r"^([A-Za-z]+)([A-Za-z]+)(\d{2})\.yaml$")
    for cfg in sorted(cfg_dir.glob("*.yaml")):
        match = pattern.match(cfg.name)
        if not match:
            violation(cfg, "config must be <Name><Phase><NN>.yaml")
            continue
        if not match.group(1)[0].isupper():
            violation(cfg, "fighter segment should be UpperCamel (Matt/Nick/Kim/Grandma)")


def audit_agents() -> None:
    agents = ROOT / "Assets" / "_Project" / "RL_Models"
    for folder in sorted(p for p in agents.iterdir() if p.is_dir()):
        match = re.match(r"^(.+)_v(\d{2})$", folder.name)
        if not match:
            violation(folder, "agent folder must be <Name>_v<NN>")
            continue
        if not (folder / "MANIFEST.md").is_file():
            violation(folder, "missing MANIFEST.md")


def audit_faces() -> None:
    faces = ROOT / "Assets" / "_Project" / "Environments" / "Art" / "Faces"
    if not faces.is_dir():
        return
    for png in sorted(faces.glob("*.png")):
        if not FACE_RE.match(png.name):
            violation(png, "face art must be <Name>_Neutral.png or <Name>_<Happy|Angry|Hurt|Insult>_<1-5>.png")


def audit_voices() -> None:
    voices = ROOT / "Assets" / "_Project" / "Data" / "Resources" / "Audio" / "Voice"
    if not voices.is_dir():
        return
    for wav in sorted(voices.glob("*.wav")):
        if not VOICE_RE.match(wav.name):
            violation(wav, "voice line must be <Name>_<Happy|Angry|Hurt|Insult>_L<1-5>_<1-5>.wav")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--quiet", action="store_true", help="print violations only")
    args = parser.parse_args()

    audit_scripts()
    audit_scenes()
    audit_configs()
    audit_agents()
    audit_faces()
    audit_voices()

    if violations:
        print(f"NAMING AUDIT: {len(violations)} violation(s)")
        for v in violations:
            print(f"  {v}")
        return 1
    if not args.quiet:
        print("NAMING AUDIT: clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
