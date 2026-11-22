"""Write diag.html (the /diag "System status" page) into a WebGL build folder.

    python scripts/webgl_diag.py Builds/WebGL 261120

Called by scripts/publish_webgl.ps1 after the Unity build, with the same yyMMdd stamp the build
carries in PlayerSettings.bundleVersion and index.html. Nothing on the page is hard-coded as
"Ready": every row is probed live from the visitor's browser (fetch + latency), from the checks
listed in scripts/webgl.json -> diagChecks, plus the WebGL2 capability and one row per file in
the build's Build/ folder (discovered here, so a renamed bundle cannot leave a stale row).
A check with a "header" key passes only when the live response carries that header, and shows
its value (the deploy workflow stamps the deployed commit into every response as X-Commit).
"""

from __future__ import annotations

import argparse
import html
import json
import sys
from pathlib import Path

SECTIONS = ("API", "Data", "Services")
CONFIG = Path(__file__).with_name("webgl.json")


def load_checks(config_path: Path = CONFIG) -> tuple[str, list[dict]]:
    """Returns (game name, diag checks) from webgl.json, validated."""
    cfg = json.loads(config_path.read_text(encoding="utf-8"))
    checks = cfg.get("diagChecks", [])
    for check in checks:
        missing = {"section", "name", "url"} - check.keys()
        if missing:
            raise ValueError(f"diagCheck {check!r} lacks {sorted(missing)}")
        if check["section"] not in SECTIONS:
            raise ValueError(f"diagCheck {check['name']!r}: section must be one of {SECTIONS}")
    return cfg.get("gameName", "Game"), checks


def bundle_checks(build_dir: Path) -> list[dict]:
    """One Data row per file the Unity loader fetches (Build/*), so /diag proves the bundle is served."""
    build = build_dir / "Build"
    if not build.is_dir():
        raise FileNotFoundError(f"{build} does not exist - build the WebGL player first")
    return [
        {"section": "Data", "name": f"Bundle file {p.name}", "url": f"Build/{p.name}", "method": "HEAD"}
        for p in sorted(build.iterdir())
        if p.is_file()
    ]


def render(game: str, stamp: str, checks: list[dict]) -> str:
    rows = json.dumps(checks)
    title = html.escape(game)
    return f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{title} - System status</title>
<style>
:root{{--bg:#0b0f17;--surface:#121826;--text:#e6e9f2;--muted:#9aa3b5;--accent:#6366f1;--ok:#34d399;--warn:#fbbf24;--bad:#f87171;--line:#232c3f}}
*{{box-sizing:border-box}}body{{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 Inter,system-ui,sans-serif;min-height:100vh;display:flex;flex-direction:column}}
header{{height:44px;display:flex;align-items:center;justify-content:space-between;padding:0 16px;background:var(--surface);border-bottom:1px solid var(--line)}}
header a{{color:var(--text);text-decoration:none;font-weight:600}}main{{flex:1;width:100%;max-width:760px;margin:0 auto;padding:16px}}
h1{{font-size:22px;margin:8px 0 12px}}h2{{font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:var(--muted);margin:20px 0 8px}}
#banner{{padding:12px 16px;border-radius:8px;font-weight:600;background:var(--surface);border:1px solid var(--line)}}
.row{{display:flex;justify-content:space-between;gap:12px;align-items:center;background:var(--surface);border:1px solid var(--line);border-radius:8px;padding:10px 14px;margin:6px 0}}
.name{{overflow-wrap:anywhere}}.st{{white-space:nowrap;font-weight:600}}.ok{{color:var(--ok)}}.warn{{color:var(--warn)}}.bad{{color:var(--bad)}}.muted{{color:var(--muted);font-weight:400}}
footer{{height:20px;display:flex;justify-content:flex-end;align-items:center;padding:0 12px;font-size:11px;color:var(--muted)}}
</style></head>
<body><header><a href="/">{title}</a><a href="/">&larr; Back to the game</a></header>
<main><h1>System status</h1><div id="banner" aria-live="polite">Checking&hellip;</div>
<section><h2>API</h2><div id="API"></div></section>
<section><h2>Data</h2><div id="Data"></div></section>
<section><h2>Services</h2><div id="Services"></div></section></main>
<footer>v{html.escape(stamp)}</footer>
<script>
const checks = {rows};
const results = [];
function row(section, name) {{
  const el = document.createElement('div'); el.className = 'row';
  el.innerHTML = '<span class="name"></span><span class="st muted">checking&hellip;</span>';
  el.firstChild.textContent = name; document.getElementById(section).appendChild(el); return el.lastChild;
}}
function done(st, ok, text) {{ st.className = 'st ' + (ok ? 'ok' : 'bad'); st.textContent = text; results.push(ok); summary(); }}
function summary() {{
  const total = checks.length + 1, bad = results.filter(r => !r).length, b = document.getElementById('banner');
  if (results.length < total) {{ b.textContent = 'Checking\\u2026 (' + results.length + '/' + total + ')'; return; }}
  b.textContent = bad === 0 ? 'All systems operational' : bad === total ? 'Down: every check failed' : 'Degraded: ' + bad + ' of ' + total + ' checks failed';
  b.className = bad === 0 ? 'ok' : bad === total ? 'bad' : 'warn';
}}
for (const s of ['API', 'Data', 'Services']) {{
  if (!checks.some(c => c.section === s) && s !== 'Services') {{
    const el = document.createElement('div'); el.className = 'row muted';
    el.textContent = 'None: the game makes no ' + s + ' calls (nothing to probe).';
    document.getElementById(s).appendChild(el);
  }}
}}
const gl = row('Services', 'WebGL2 renderer in this browser');
let ctx = null; try {{ ctx = document.createElement('canvas').getContext('webgl2'); }} catch (e) {{}}
done(gl, !!ctx, ctx ? 'Available' : 'Unavailable');
for (const c of checks) {{
  const st = row(c.section, c.name), t0 = performance.now();
  fetch(c.url, {{ method: c.method || 'GET', cache: 'no-store' }})
    .then(r => {{
      const ms = Math.round(performance.now() - t0) + ' ms';
      if (!c.header) return done(st, r.ok, (r.ok ? 'OK ' : 'HTTP ' + r.status + ' ') + ms);
      const v = r.headers.get(c.header) || '';
      done(st, r.ok && v.length > 0, r.ok ? (v ? v.slice(0, 7) + ' (' + ms + ')' : 'Missing ' + c.header) : 'HTTP ' + r.status + ' ' + ms);
    }})
    .catch(e => done(st, false, 'Unreachable (' + Math.round(performance.now() - t0) + ' ms)'));
}}
</script></body></html>
"""


def write(build_dir: Path, stamp: str, config_path: Path = CONFIG) -> Path:
    game, checks = load_checks(config_path)
    page = render(game, stamp, checks + bundle_checks(build_dir))
    out = build_dir / "diag.html"
    out.write_text(page, encoding="utf-8")
    return out


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("build_dir", type=Path)
    parser.add_argument("stamp", help="yyMMdd build stamp, the same one the player and index.html carry")
    args = parser.parse_args(argv)
    if not (len(args.stamp) == 6 and args.stamp.isdigit()):
        parser.error(f"stamp must be yyMMdd, got {args.stamp!r}")
    print(write(args.build_dir, args.stamp))
    return 0


if __name__ == "__main__":
    sys.exit(main())
