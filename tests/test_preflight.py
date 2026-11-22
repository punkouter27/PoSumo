"""preflight.py reads the real project files: the physics every brain was fitted against must hold."""

import os

import preflight


def test_committed_physics_matches_what_the_brains_were_fitted_at():
    assert preflight.physics_problems() == []


def test_code_contract_reads_observation_and_action_sizes_from_agent_biped():
    base, blocks, actions = preflight.code_contract()
    assert base > 0 and actions > 0
    assert set(preflight.FLAGS) <= set(blocks)


def test_every_shipped_fighter_has_a_character_sheet():
    agents = os.path.join(preflight.ROOT, "Assets", "_Project", "RL_Models")
    names = [d[: -len("_v01")] for d in os.listdir(agents) if d.endswith("_v01")]
    assert names, "no Assets/_Project/RL_Models/<Name>_v01 folders"
    for name in names:
        values, path = preflight.sheet(name)
        assert values is not None, f"missing {path}"


def test_sha_is_stable_and_short():
    assert preflight.sha("a", "b") == preflight.sha("a", "b")
    assert len(preflight.sha("a")) == 12
