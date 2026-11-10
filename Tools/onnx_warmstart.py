"""Turn a SHIPPED brain (.onnx) into an ML-Agents warm-start checkpoint, optionally
widening its input layer with zero-weighted columns.

Why: Training/results/ is gitignored and absent from a fresh clone, so the deployed
.onnx files are the only weights that exist. An ML-Agents ONNX export keeps the
PyTorch parameter names of the actor (network_body..., action_model...), so the
policy can be rebuilt exactly. What an export does NOT carry:

  * the critic and the optimiser state -> the trainer logs "Failed to load for
    module Optimizer:..." and initialises them fresh. Expected.
  * the normaliser's step count -> only sqrt(variance) survives, constant-folded
    into the Div node. A synthetic count (--norm-steps) is written; a larger count
    makes the carried statistics adapt more slowly.

Widening (--new-obs N) follows the frozen-interface rule (AGENTS.md section 3):
inputs are APPENDED, their first-layer weights start at ZERO and their normaliser
stats at mean 0 / variance 1, so the widened policy computes exactly what the
shipped one did until training moves those weights. The script asserts that.

    Training/venv/Scripts/python.exe Tools/onnx_warmstart.py \
        Assets/Agents/Matt_v01/Matt.onnx Training/results/matt_intentinit/Matt/checkpoint.pt --new-obs 2
"""
import argparse
import os
import sys

import numpy as np
import onnx
import torch
from onnx import numpy_helper

from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from mlagents_envs.base_env import ActionSpec, DimensionProperty, ObservationSpec, ObservationType

NORM = "network_body.observation_encoder.processors.0.normalizer."
FIRST = "network_body._body_endoder.seq_layers.0.weight"


def load_onnx(path):
    model = onnx.load(path)
    weights = {i.name: numpy_helper.to_array(i) for i in model.graph.initializer}
    std = None
    for node in model.graph.node:
        if node.op_type == "Div" and node.name.endswith("normalizer/Div"):
            std = weights[node.input[1]]
    if std is None:
        sys.exit("no normaliser Div node found - was this brain trained with normalize: false?")
    return weights, std


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("onnx")
    parser.add_argument("out")
    parser.add_argument("--new-obs", type=int, default=0)
    parser.add_argument("--hidden", type=int, default=512)
    parser.add_argument("--layers", type=int, default=3)
    parser.add_argument("--norm-steps", type=float, default=1_000_000.0)
    args = parser.parse_args()

    weights, std = load_onnx(args.onnx)
    old_obs = weights[NORM + "running_mean"].shape[0]
    actions = weights["action_model._continuous_distribution.mu.bias"].shape[0]
    new_obs = old_obs + args.new_obs

    spec = [ObservationSpec(name="obs_0", shape=(new_obs,),
                            dimension_property=(DimensionProperty.NONE,),
                            observation_type=ObservationType.DEFAULT)]
    actor = SimpleActor(spec, NetworkSettings(normalize=True, hidden_units=args.hidden,
                                              num_layers=args.layers),
                        ActionSpec.create_continuous(actions))
    state = actor.state_dict()

    steps = args.norm_steps
    for key in list(state.keys()):
        if key == NORM + "running_mean":
            mean = np.zeros(new_obs, np.float32)
            mean[:old_obs] = weights[key]
            state[key] = torch.from_numpy(mean)
        elif key == NORM + "running_variance":
            var = np.full(new_obs, steps, np.float32)          # new slots: variance 1
            var[:old_obs] = (std.astype(np.float64) ** 2 * steps).astype(np.float32)
            state[key] = torch.from_numpy(var)
        elif key == NORM + "normalization_steps":
            state[key] = torch.tensor(int(steps))
        elif key == FIRST:
            w = np.zeros((args.hidden, new_obs), np.float32)   # new columns: zero
            w[:, :old_obs] = weights[key]
            state[key] = torch.from_numpy(w)
        elif key in weights:
            src = torch.from_numpy(np.array(weights[key]))
            if tuple(src.shape) != tuple(state[key].shape):
                src = src.reshape(state[key].shape)
            state[key] = src
    missing = [k for k in weights if k.startswith(("network_body", "action_model")) and k not in state]
    if missing:
        sys.exit(f"ONNX weights with no home in the actor: {missing}")
    actor.load_state_dict(state)

    # Equivalence check: the widened actor must reproduce the shipped brain's
    # deterministic action for any value of the new slots.
    rng = np.random.default_rng(0)
    obs = rng.normal(size=(64, old_obs)).astype(np.float32)
    extra = rng.normal(size=(64, args.new_obs)).astype(np.float32) * 3
    x = np.clip((obs - weights[NORM + "running_mean"]) / std, -5, 5)
    for layer in (0, 2, 4):
        x = x @ weights[f"network_body._body_endoder.seq_layers.{layer}.weight"].T \
            + weights[f"network_body._body_endoder.seq_layers.{layer}.bias"]
        x = x / (1 + np.exp(-x))                                # swish
    ref = x @ weights["action_model._continuous_distribution.mu.weight"].T \
        + weights["action_model._continuous_distribution.mu.bias"]
    with torch.no_grad():
        encoding, _ = actor.network_body([torch.from_numpy(np.concatenate([obs, extra], 1))])
        mu = actor.action_model._continuous_distribution.mu(encoding).numpy()
    err = float(np.abs(mu - ref).max())
    if err > 1e-3:
        sys.exit(f"EQUIVALENCE FAILED: max |mu - shipped| = {err}")

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    torch.save({"Policy": actor.state_dict(), "global_step": {"current_step": torch.tensor(0)}}, args.out)
    print(f"WARMSTART RESULT: {args.onnx} -> {args.out} | obs {old_obs}->{new_obs} "
          f"actions {actions} | max |mu - shipped| = {err:.2e}")


if __name__ == "__main__":
    main()
