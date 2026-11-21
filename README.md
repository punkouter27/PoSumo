# PoSumo

A 2D side-view sumo game. Each wrestler is a ragdoll of 13 hinge joints driven by its own Unity ML-Agents
policy (ONNX, inference only at runtime); a tournament bracket of RL-trained fighters plays itself.

- Play: https://thankful-beach-0a07eb70f.2.azurestaticapps.net/ (status page: `/diag`)
- Unity 6000.6.9f1, URP 2D, UI Toolkit, ML-Agents 4.1.0 (vendored and patched under `Training/ml-agents`)
- Controls: the game plays itself; `Tab` (or the DIAG corner button) toggles the diagnostics overlay.
  Leave it alone for 30 s and it switches to demo mode (cycling cameras); any input returns.

| Path | What |
|---|---|
| `Assets/` | the Unity project (scenes `SCN_TOURNAMENT` → `SCN_SUMO`, scripts by prefix `Agent_`/`Reward_`/`Sensor_`/`Systems_`) |
| `Training/` | trainer configs (`configs/*.yaml`), launch scripts, the vendored ML-Agents |
| `scripts/` | `publish_webgl.ps1` (tests → build → release → deploy), `deploy_webgl.ps1`, Python tooling |
| `tests/` | pytest suite for the Python tooling (`ruff check . && ruff format --check . && pytest`) |
| `docs/` | design notes, training write-ups, project history (`project-notes.md`) |

Agent and contributor rules: [AGENTS.md](AGENTS.md).
