"""The graduation gate's streak and NaN checks (pure functions over TensorBoard scalar series)."""

import math

import graduation_gate


def test_streak_counts_back_to_the_last_failure():
    series = {
        "Gate/WalkEpisodes": {100: 5.0, 200: 7.0, 300: 4.0},
        "Gate/WalkFailures": {100: 0.0, 200: 1.0, 300: 0.0},
    }
    # Newest first: step 300 is clean (4 episodes), step 200 had a failure, so the streak stops there.
    assert graduation_gate.streak(series, "Walk") == (4, 3)


def test_streak_without_failures_sums_everything():
    series = {"Gate/WalkEpisodes": {1: 2.0, 2: 3.0}}
    assert graduation_gate.streak(series, "Walk") == (5, 2)


def test_nan_scalars_only_flags_loss_and_policy_tags():
    series = {
        "Losses/Value Loss": {1: 0.5, 2: math.nan},
        "Policy/Entropy": {1: 1.0},
        "Environment/Cumulative Reward": {1: math.inf},
    }
    assert graduation_gate.nan_scalars(series) == ["Losses/Value Loss"]
