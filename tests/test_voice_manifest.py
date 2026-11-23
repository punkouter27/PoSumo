"""voices.json covers every roster fighter x 5 emotions x 5 levels, and its exists flags match the disk."""

import json
import re
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
VOICE_ROOT = REPO / "Assets" / "_Project" / "Data" / "Resources" / "Audio" / "Voice"
MANIFEST = VOICE_ROOT / "voices.json"
EMOTIONS = ["angry", "happy", "hurt", "insult", "celebrate"]
STANDARD = re.compile(r"^([a-z0-9]+)/(angry|happy|hurt|insult|celebrate)-([1-5])\.wav$")
LEGACY = re.compile(r"^([A-Za-z0-9]+)_(Angry|Happy|Hurt|Insult|Celebrate)_L([1-5])_[1-5]\.wav$")


def roster_names():
    names = []
    for asset in sorted((REPO / "Assets" / "_Project" / "RL_Models").glob("*_v*/*_Character.asset")):
        match = re.search(r"^\s*behaviorName:\s*(\S+)\s*$", asset.read_text(encoding="utf-8"), re.MULTILINE)
        assert match, asset
        names.append(match.group(1))
    return names


def test_manifest_lists_every_fighter_five_by_five():
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    assert manifest["schema"] == "po-voice-manifest/1"
    assert manifest["emotions"] == EMOTIONS
    assert manifest["levels"] == [1, 2, 3, 4, 5]
    by_id = {character["id"]: character for character in manifest["characters"]}
    names = roster_names()
    assert len(names) == 16
    assert set(by_id) == {name.lower() for name in names}
    for name in names:
        character = by_id[name.lower()]
        assert character["voice"]["pitchHz"] > 0
        assert character["voice"]["formantScale"] > 0
        for emotion in EMOTIONS:
            cells = character["clips"][emotion]
            assert [cell["level"] for cell in cells] == [1, 2, 3, 4, 5]
            for cell in cells:
                standard = STANDARD.match(cell["file"])
                legacy = LEGACY.match(cell["file"])
                if standard:
                    assert standard.group(1) == name.lower() and standard.group(2) == emotion
                    assert int(standard.group(3)) == cell["level"]
                else:
                    assert legacy, cell["file"]
                    assert legacy.group(1) == name and legacy.group(2).lower() == emotion
                    assert int(legacy.group(3)) == cell["level"]
                assert (VOICE_ROOT / cell["file"]).is_file() == cell["exists"], cell["file"]


def test_every_fighter_has_a_distinct_voice():
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    profiles = [json.dumps(character["voice"], sort_keys=True) for character in manifest["characters"]]
    assert len(set(profiles)) == len(profiles)
