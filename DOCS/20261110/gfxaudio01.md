# 2026-11-10 — GFX / audio / physics-audit pass

Request: the "GFX & AUDIO — Top 10" list (shaders, GI, SSR, particles, CCD and mass
ratios, procedural impact audio, friction audio, HRTF, headless Blender, Unreal).
Triaged against what a 2D URP, sagittal-ragdoll, Android-first Unity game can use;
the user answered "do all" to the five items that survived triage.

## Policies touched (AGENTS.md §3)

| Change | Kind | Policies touched |
|---|---|---|
| `Systems_ImpactSynth` — modal impact synthesis (energy, stiffness, material pair) replaces the `SFX_Thud*` / `SFX_Scuff` one-shots | audio (presentation) | none |
| `Systems_FootScrape` + `Systems_FrictionChatter` — scrape tone follows shear speed, stick-slip chatter on the audio thread | audio (presentation) | none |
| `Systems_DesktopFx` — desktop-only bloom / dispersion / grain / 2x particles; `Systems_PostFx` gains an aberration baseline; `Systems_DustPuff.SetDensity` | visuals | none |
| `Systems_ClayWear` — lasting scuff streaks and landing dents, tournament-persistent | visuals | none |
| `Systems_PhysicsAudit` — dev-only, read-only penetration / tunnelling / Simulate-cost measurement | tooling | none |

No observation, action, mass, collider, solver setting or referee rule moved. Every
new system is a spawned companion behind its own `GameTuning` flag
(`enableImpactSynth`, `enableFootScrape`, `enableClayWear`, `enableDesktopFx`,
`enablePhysicsAudit`). The flags default ON in code; `GameTuning.asset` does not list
them yet, so the code default is what runs until the asset is next saved.

## Not done, and why (so nobody re-derives it)

| Asked for | Why not here |
|---|---|
| Unreal shaders / lighting | Unity project; would be a full port |
| SSGI / RTX GI, SSR, contact shadows, volumetric refraction | URP 2D renderer: sprites write no depth, no reliable opaque texture, and the only floor in frame is the dohyo's front face edge-on (see CLAUDE.md, cast shadows) |
| HRTF binaural + geometric reverb | `spatialBlend = 0` is deliberate in an orthographic, zooming camera (CLAUDE.md, GFX/sound pass) |
| Headless Blender LOD / bake / .glb | the bodies are code-built sprites; there are no meshes |
| CCD | already on: `Agent_BipedBody` sets `CollisionDetectionMode2D.Continuous` on every part |
| Mass-ratio / collider changes | would invalidate all four brains; the audit below measures first |

## Verification

- Compiles clean: Unity's Roslyn standalone against `PoSumo.Runtime.rsp`, then the
  Editor (`scriptCompilationFailed` false). Console: only the known IvanMurzak relay
  auth error.
- `BRACKET HARNESS RESULT: PASS — champion Kim over 15 bouts (16 fighters, 0 byes) in 474s`
  with every new flag on.
- Live check in a bout: `desktop=True synth=True wear=10 audit=True`.
- Clay wear resized after the first run: 2.5-4 cm marks every 5 cm of slide hit the
  700 cap within 13 bouts and read as a hairline; now 5-8 cm every 12 cm, 66 marks
  after 4 bouts, visible on the mat lip.
- Screens, audio samples and report: `SCREENSHOTS/20261110_gfxaudio/report.html`.

## Physics audit — 15 bouts, 18 307 live steps

| Lane | pair samples | >5 mm | >2 cm | max | worst |
|---|---|---|---|---|---|
| fighter vs fighter | 5 290 723 | 0.23 % | 3 | 36.6 mm | toe/toe |
| fighter vs arena | 2 489 752 | 2.73 % | 13 | 33.4 mm | foot/platform |
| self (30 leg-trunk pairs) | 1 098 420 | 3.46 % | **35 359** | **102 mm** | ThighNear/Chest, ThighNear/LowerBack |

Tunnelling risk (fastest part travel per step / thinnest collider, 6 cm) peaks at
2.93 with CCD on, and shows no visible overlap. `Physics2D.Simulate` costs 0.148 ms
per step on average; the 19.6 ms worst case falls on a scene-load step.

**Open issue:** the near thigh sinks up to 10 cm into the chest and lower back. The
count of deep overlaps is nearly constant per bout (~2 400), so the likely cause is
the crouched start pose (`Agent_BipedBody.startCrouched` / `ApplyCrouch`) rather
than fighting. Any fix moves the pose every brain starts from, so it needs a
retrain. It is left unchanged.

**CONFIRMED the same day.** `Systems_PhysicsAudit` now logs `SELF OVERLAP TIMING:`,
which splits the deep self overlaps by time since the round went live. Over four bouts,
including one of 38 s, every step of the first ~3 s overlapped and then almost none
did. A frame probe of the opening shows why:

    0.0-2.9 s  FROZEN (simulated=false)  ThighNear-Chest -4 cm  hip -120 (its limit)
    3.0 s      released                  0 cm
    3.1-3.4 s  simulated                 hip opens to ~-95, gap 1 -> 11 cm
    3.7 s+     simulated                 gap 32-40 cm, hip -43 -> +32

Cause: `Agent_BipedBody.CROUCH_ANGLES` puts the hip at its -120 limit with 90 deg of
forward spine pitch. That FK pose places the near thigh inside the trunk (4 cm into the
chest, up to 10 cm into the lower back). The referee holds it FROZEN through the 3 s
countdown, so it is drawn interpenetrating for the whole hold. On release the solver
pushes the thigh out within ~0.1 s, which is a small energy pop at the start of the
tachiai. After that the fight never reproduces it.

Proposed fix (not applied): open the crouch's hip, e.g. hip -100 / knee 100 /
ankle -20, which keeps the feet flat (pelvis - hip - knee - ankle = 0). Verify zero
self overlap in the frozen pose, then run a short corrective pass on each fighter,
because both referees start every bout from this pose.
