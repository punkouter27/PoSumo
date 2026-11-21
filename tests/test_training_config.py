"""The trainer YAML and the C# agent must name the same reward-penalty environment parameters."""

import pathlib
import re

import pytest

yaml = pytest.importorskip("yaml")

ROOT = pathlib.Path(__file__).resolve().parents[1]
PENALTIES = ("joint_torque_penalty", "joint_velocity_penalty", "action_rate_penalty", "slip_penalty")


def agent_parameters():
    source = (ROOT / "Assets" / "Scripts" / "Agent" / "Agent_Biped.cs").read_text(encoding="utf-8")
    return set(re.findall(r'GetWithDefault\("([a-z0-9_]+)"', source))


@pytest.mark.parametrize("config", sorted((ROOT / "Training" / "configs").glob("*Real02.yaml")), ids=lambda p: p.name)
def test_current_configs_set_every_standard_penalty(config):
    params = yaml.safe_load(config.read_text(encoding="utf-8"))["environment_parameters"]
    for name in PENALTIES:
        assert name in params, f"{config.name} lacks {name}"
        assert 0 <= float(params[name]) < 0.05, f"{config.name}: {name} looks like a typo"


def test_agent_reads_every_penalty_the_configs_set():
    missing = set(PENALTIES) - agent_parameters()
    assert not missing, f"Agent_Biped.cs never reads {sorted(missing)}"
