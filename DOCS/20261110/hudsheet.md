# HUD sheet pass — 2026-11-10

Source: the "UI — Top 10 Options" canvas (https://claude.ai/artifact/SiLHYVKhhqQNmQCkcMCHXT);
the user picked "do all" on option 01 + the flow boards (06-09).

## Policies touched

**None.** Visuals only (UU_AGENT §3: "visuals → none"). No observation, action, mass,
collider or referee rule moved. The one referee change is a read-only property,
`Systems_GameMatchManager.ResultShowing`.

## What changed

| Corner / band | Before | After |
|---|---|---|
| TC | frame rate, hidden until DBG | frame rate always; `· step N` (physics steps) while debug is open |
| BL | `DBG` chip | live telemetry line, which is also the debug button (`Systems_UiKit.StreamButton`) |
| BR | `v1.0.4` | build date `YYMMDD` (`Systems_BuildStamp`, written by `Assets/Editor/WriteBuildStamp.cs`) |
| Dock | card over the crowd | bottom sheet (`Systems_HudSheet`): grabber + MENU › TACHIAI › FIGHT › RESULT strip |
| Settings › DISPLAY | — | `BUILD v1.0.4 · 261110` |

Defaults chosen without asking (the user said "do all"): the date replaces the version
in the corner; DBG leaves the corner (reach it with TAB, by tapping the telemetry line,
or from Settings); the sheet stays at its small height during a fight.

## Verification

- `scriptCompilationFailed` false; console 0 errors after a played bout.
- `Tools/portrait_check.py --sizes 1080x2400`: 0 overflow findings (bracket, arena,
  pause, notch-sim). `HudOverflowAudit.Corners` still passes; names are unchanged.
- Report: `SCREENSHOTS/20261110_hudsheet/report.html`.
- Not run: `BracketTestHarness` (full 15 bouts) and an on-device APK.

## Not built from the mockups

- Tabs inside the sheet (FIGHT / FIGHTERS / BRACKET / LOG). The dock holds one card at
  MINIMAL density, so tabs would switch between one thing and nothing.
- Options 02-05 and 10. Only option 01 was implemented.
