# 2026-11-10 — clean pass

## Policies touched (AGENTS.md §3)

| Change | Kind | Policies touched |
|---|---|---|
| MAT meter / round footer hidden when inert, scorebug "VS", FPS behind DBG | visuals | none |
| Deleted `Builds/`, never-run configs, dead launchers, stale logs | tooling | none |

No observation, action, mass, collider or referee rule moved.

## Verification

- Compiles clean (`scriptCompilationFailed` false); console shows only the known
  font-reference and MCP-relay-auth noise.
- `portrait_check.py --sizes 1080x2400`: 0 findings (notch pass included).
- `ref_audit.py`: every GUID resolves. `naming_audit.py`: clean.
- Screens: `SCREENSHOTS/20261110_clean/{before,after}/`, report `report.html`.

## Next

`Builds/` is gone: rebuild the four envs (*PoSumo → Build <Name> Training Env*)
before any Intent02 run.
