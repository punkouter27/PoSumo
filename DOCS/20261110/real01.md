# Real01 — the realism pass (2026-11-10)

The ten options from the "AI animal realism" list, built for PoSumo's 2D Box2D
ragdoll. **PoSumo has no MuJoCo**, so the MuJoCo-specific parts of that list
(native muscle actuators, `subtree_com`, soft weld constraints) are mapped to
their Unity equivalents below rather than adopted.

**Nothing here changes a shipped brain or the game.** Every new input, reward
term and training aid is off by default: switched on by a character-sheet flag
that stays off, or by an ML-Agents environment parameter that only a training
config sets. Verified after the change: 44/44 EditMode tests and the bracket
harness (results at the end).

## Policies touched (AGENTS.md rule 3, "before any change")

| Change | Policies it touches | Interface change? |
|---|---|---|
| Balance observations (+2 inputs) | all 4 trained brains, only when retrained with `Real01`; the 12 placeholders follow their donors | yes, 53 → 55, **opt-in** |
| Slip penalty, second-difference penalty | the brains trained under `Real01` | no (reward only) |
| Sensor delay, mass/damping jitter | the brains trained under `Real01` | no (training conditions) |
| Standing aid, walk-lane bales | the walk half of each unified brain, `Real01` only | no |
| Preflight, stamp, graduation gate, deploy refusal | none (tooling) | no |
| Realism and gate stats on TensorBoard | none (logging) | no |

## The ten, as built

| # | Option | Where | Default |
|---|---|---|---|
| 1 | Charge a loaded foot that slides | `Reward_FootSlip` (pure, unit-tested) → `slipPenalty` / `walkSlipPenalty` on the sheet, env params `slip_penalty` / `walk_slip_penalty` | 0 |
| 2 | Balance observations: CoM and capture point vs. the middle of the feet | `Agent_Biped.balanceObservations`, `BalanceOffset`; `Agent_BipedBody.CenterOfMass` / `CenterOfMassVelocity`; training env forced on by `-posumoBalanceObs` (`Systems_TrainingFlags`) | off |
| 3 | Second-order smoothing + an oscillation metric | `jerk2Penalty` / `walkJerk2Penalty` (mean abs(a(t) − 2a(t−1) + a(t−2))); `Realism/HighFreqShare` on TensorBoard (share of joint motion above ~5 Hz) | 0 |
| 4 | Muscle power cap | **not built — already covered.** The Hill curve in `ApplyMotor` is (1−s)/(1+s), whose power peaks at 17% of F0·v0: about 230 W at the hip against a human's ~1000 W. A human-scale watt cap would never bind. | — |
| 5 | Sensor delay + body randomisation | `obs_delay_max` (0–2 decisions, drawn per episode, buffered in `EmitObservations`), `mass_jitter`, `damping_jitter` (`Agent_BipedBody.RandomizePhysique`, always from the built values) | 0 |
| 6 | Staged curriculum | `Training/configs/<Name>Real01.yaml`: balance → locomotion → perturbation → obstacles on one progress schedule | config |
| 7 | Bales across the walk lane | `Systems_WalkObstacles`, env params `walk_obstacle_height` / `walk_obstacle_count` | 0 |
| 8 | Standing aid that fades | `Agent_Biped.ApplyStandAssist`, env param `stand_assist` (fraction of body weight; Real01 0.5 → 0.25 → 0) | 0 |
| 9 | Preflight + version stamp | `Tools/preflight.py`, called by both training wrappers; writes `Training/stamps/<run>.json` | always on |
| 10 | Graduation gate | `Tools/graduation_gate.py` writes `GATE.json`; `DeployBrain` refuses a run without a passing one unless *PoSumo → Allow Ungated Brain Deploy* is ticked | always on |

## Things to know before running it

- **Every env must be rebuilt.** `Builds/` is empty, and the preflight refuses an env
  older than the newest script.
- **Warm start** widens each shipped brain by two zero-weighted inputs:
  `Tools/onnx_warmstart.py Assets/Agents/<Name>_v01/<Name>.onnx Training/results/<name>_realinit/<Name>/checkpoint.pt --new-obs 2`,
  then `Start-StaminaExtension.ps1 -Phase Real01 -InitializeFromPhase Realinit -WidenedTrunk`.
- **Calibrate the two smoothness costs** after the first summaries: the starting values
  (slip 0.002, jerk2 0.0003) are guesses until `Realism/FootSlip` and
  `Realism/HighFreqShare` show the real ranges. AGENTS.md shaping trap 1.
- **The standing aid is anti-gravity** and so lives only in training's walk lane,
  scheduled to 0 by 70% progress. The game's own postural reflex (a torque, not a
  lift) is unchanged.
- **The gate is literal.** `--school walk` (100 graduations without a fall) is
  reachable. `--school sumo` (100 straight wins against a self-play pool of similar
  strength) is not, by design; the ELO curve stays the fight's accept signal.
  Short runs (the Unique01 waves) will be refused at deploy until the toggle is
  ticked; that is the rule working.
- **Gate stats exist only in envs built after this change.** Older runs report
  "no Gate/WalkEpisodes recorded".
- Balance observations are deployed by `DeployBrain`, which reads the new model's
  input width and sets `balanceObservations` on the fighter's sheet **and on every
  placeholder that borrows that brain**.

## Verification

- Compile clean; `scriptCompilationFailed` false; zero console errors.
- EditMode: 44/44 (38 before + 6 `FootSlipTests`).
- Preflight dry run: `MattReal01` resolves 55 obs / 14 actions, reads 53 from the
  shipped `Matt.onnx`, refuses on the missing env build. `MattIntent01` resolves 53.
- Gate dry run on a copy of `matt_intent01`: FAIL, "no Gate/WalkEpisodes recorded",
  no NaN found in its scalars or checkpoint.
- Bracket harness on SCN_TOURNAMENT: `BRACKET HARNESS RESULT: PASS`, 15 bouts, 0 errors, 0 deep self-overlap steps. One pre-existing `[OBS] RANGE` warning (a joint-speed slot at -5.3), unrelated: the vector order and values are unchanged.
