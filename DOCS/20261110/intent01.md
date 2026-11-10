# 2026-11-10 — features #3, #7, #9, #10 and the Intent01 run

## Policies touched (AGENTS.md §3 "before any change")

| Change | Kind | Policies touched |
|---|---|---|
| #3 fighter silhouette outline (`Systems_FighterOutline*`, `PoSumo_FighterOutline.shader`, renderer feature on `Renderer2D.asset`) | visuals | none |
| #7 tachiai face-off on Timeline (`Systems_TachiaiCeremony`, `Systems_Ceremony*`) | visuals | none |
| #9 opponent-intent sensing (`intentObservations`, +2 slots appended, 51 → 53) | observation interface | Matt, Grandma, Nick, Kim (and the 12 placeholders that borrow them) |
| #10 edge-brace reward (`edgeBraceReward`, `Reward_SumoObjective.EdgeBraceFactor`) | reward | Matt, Grandma, Nick, Kim |

Interface version after this change: **53 obs / 14 actions / decision period 3**.
Physics version: unchanged (no mass, collider, joint or referee rule moved).

## Warm start

No `Training/results` existed, so the four shipped `.onnx` were converted back into
ML-Agents checkpoints by `Tools/onnx_warmstart.py`, widening the input layer with two
zero-weighted columns. Verified: the widened actor reproduces the shipped brain's
deterministic action to within 2.6e-6 for any value in the new slots. The critic,
optimiser state and normaliser step count are not in an ONNX and start fresh.

## Run

`<Name>Intent01.yaml`, run ids `<name>_intent01`, `--initialize-from <name>_intentinit`,
1 env each (memory: 8.8 GB free with the Editor closed), all four concurrently,
auto-stop at 60 minutes.

## Result (12:58)

| Fighter | Steps (TB) | ELO start → end | Round s start → end | Deployed checkpoint |
|---|---|---|---|---|
| Matt | 870k | 1201 → 1392 | 14.6 → 17.4 | `Matt-749794.onnx` |
| Grandma | 690k | 1199 → 1278 | 14.2 → 15.8 | `Grandma-499508.onnx` |
| Nick | 690k | 1201 → 1260 | 14.4 → 16.0 | `Nick-499857.onnx` |
| Kim | 690k | 1193 → 1219 | 11.3 → 15.6 | `Kim-499934.onnx` |

All four ELO curves rose (accept on shape). The auto-stop killed the trainers rather
than closing them, so no final `<Behavior>.onnx` was written; the newest numbered
checkpoint was deployed instead (the last ~120-190k steps of each run are lost).
Rounds got LONGER on every fighter — consistent with the edge brace holding ground at
the rim, but it is also the shape a stall would take; watch it in play.

Charts: `intent01_report.html`.

## Placeholders

The 12 placeholders each received their OWN copy of a randomly drawn donor brain
(*PoSumo → Copy Brains To Placeholders (Random)*, seed 10119140), at
`Assets/Agents/<Name>_v01/<Name>.onnx`, to be retrained into styles of their own:
Alan, Dupee, Porto, Tong ← Matt · Brendan, Maya, Velez ← Grandma · Baily, Josh ← Nick ·
Decoy, Manzi, Sokol ← Kim.
