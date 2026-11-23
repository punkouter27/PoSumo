# PoSumo

A 2D side-view sumo game. Each wrestler is a ragdoll of 13 hinge joints driven by its own Unity ML-Agents
policy (ONNX, inference only at runtime); a tournament bracket of RL-trained fighters plays itself.

- Play: https://thankful-beach-0a07eb70f.2.azurestaticapps.net/ (status page: `/diag`)
- Unity 6000.6.9f1, URP 2D, UI Toolkit, ML-Agents 4.1.0 (vendored and patched under `Training/ml-agents`)
- Controls: the game plays itself; `Tab` (or the DIAG corner button) toggles the diagnostics overlay.
  Leave it alone for 30 s and it switches to demo mode (cycling cameras); any input returns.

## Tournament standard (2026-11-22)

- **Bracket only, no free-for-all.** PoSumo is a 1v1 sumo game, so the cross-project standard's FFA
  mode does not apply here; every tournament is the 16-slot single-elimination bracket.
- **Seeding by rating** (tournament Elo, `Systems_TournamentElo`: start 1000, K 32, saved to
  `tournament_elo.json`). First round: 1v16, 8v9, 4v13, 5v12, 2v15, 7v10, 3v14, 6v11. Empty slots
  are **BYE** and take the bottom seeds; a BYE's opponent advances unfought, BYE v BYE sends a BYE on,
  a drawn bout goes to the higher seed, and the bracket plays itself.
- **Main menu**: the BRACKET tab draws the tree, the GRID tab shows the 16 slots as a 4x4 grid in
  seed order (BYE tiles for empty slots). All 16 roster fighters have their own trained brains
  (the 12 former placeholders were promoted in Unique01, 2026-11-11), so a full roster draws no BYEs.
- **Voice matrix**: 5 emotions (angry, happy, hurt, insult, celebrate) x 5 levels per fighter,
  listed in the voice manifest (below).

## Voices: `voices.json`

`Assets/_Project/Data/Resources/Audio/Voice/voices.json` lists every fighter x 5 emotions x 5 levels:
the WAV each cell uses, whether it `exists`, and the fighter's procedural voice profile (pitch,
formant scale, breath, rasp, vibrato, tempo). Cells use `<id>/<emotion>-<level>.wav` (id = lower-case
fighter name); where an older recording is what actually plays, the cell lists that real file
(`<Fighter>_<Mood>_L<level>_<take>.wav`, with all `takes`). Today 60 of 400 cells are recorded.

- **Add a recording**: drop `matt/angry-3.wav` into that folder. The manifest regenerates on import
  (or run *PoSumo → Voice → Regenerate voices.json*); it logs "N of M clips recorded; missing: ...".
  Never edit `exists` by hand; voice profiles in the file are kept on regeneration, so tune them there.
- **If a WAV is missing** the game plays an advanced procedural imitation of that fighter
  (`Systems_VoiceSynth`: glottal pulses with jitter/shimmer, four gliding formants, consonant onsets,
  per-emotion prosody, level-scaled loudness and length), deterministic and cached per line.

| Path | What |
|---|---|
| `Assets/` | the Unity project (scenes `SCN_TOURNAMENT` → `SCN_SUMO`, scripts by prefix `Agent_`/`Reward_`/`Sensor_`/`Systems_`) |
| `Training/` | trainer configs (`configs/*.yaml`), launch scripts, the vendored ML-Agents |
| `scripts/` | `publish_webgl.ps1` (tests → build → release → deploy), `deploy_webgl.ps1`, Python tooling |
| `tests/` | pytest suite for the Python tooling (`ruff check . && ruff format --check . && pytest`) |
| `docs/` | design notes, training write-ups, project history (`project-notes.md`) |

Agent and contributor rules: [AGENTS.md](AGENTS.md).
