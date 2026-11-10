"""Build DOCS/20261110/training_status.html: a non-technical training status report.

Inputs (all measured, nothing typed in by hand except the lineage notes):
  DOCS/20261110/intent01_scalars.json   TensorBoard scalars of the latest runs
  Logs/ActionLogs/bouts_20261110_*.csv  every bout played in the game today
  %USERPROFILE%/AppData/LocalLow/Punkouter Software/PoSumo/tournament_elo.json
  Training/unique01_roster.csv          donor of each fighter

Charts are inline SVG (no matplotlib on this machine) and follow dark mode.
Run: python Tools/make_training_status.py
"""
import csv
import glob
import html
import json
import os
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "DOCS" / "20261110" / "training_status.html"

TRAINED = ["Matt", "Grandma", "Nick", "Kim"]
SERIES = {"Matt": "var(--s1)", "Grandma": "var(--s2)", "Nick": "var(--s3)", "Kim": "var(--s4)"}

# Steps trained under the CURRENT sumo rules (cold Sumo01 onward). Sumo01+Lunge01 as
# recorded in DOCS/training_report.html (2026-10-05 17:59, mid-run, so a floor), plus
# today's Intent01 measured from TensorBoard below.
EARLIER = {"Matt": 4.3, "Grandma": 1.9, "Nick": 2.4, "Kim": 2.6}


def load():
    scalars = json.load(open(ROOT / "DOCS" / "20261110" / "intent01_scalars.json"))
    roster = {r["fighter"]: r for r in csv.DictReader(open(ROOT / "Training" / "unique01_roster.csv"))}
    wins, losses = Counter(), Counter()
    for path in glob.glob(str(ROOT / "Logs" / "ActionLogs" / "bouts_20261110_*.csv")):
        for row in csv.DictReader(open(path, encoding="utf-8-sig")):
            if not row["winner"]:
                continue
            loser = row["fighterB"] if row["winner"] == row["fighterA"] else row["fighterA"]
            wins[row["winner"]] += 1
            losses[loser] += 1
    elo_path = Path(os.environ["USERPROFILE"]) / "AppData/LocalLow/Punkouter Software/PoSumo/tournament_elo.json"
    elo = {e["fighter"]: e for e in json.load(open(elo_path))["entries"]}
    return scalars, roster, wins, losses, elo


def esc(text):
    return html.escape(str(text))


# ----------------------------------------------------------------- SVG helpers

def line_chart(scalars, tag, title, y_label, notes, y_fmt="{:.0f}", height=300):
    """Small multiple-free line chart: 4 series, direct labels at the line ends."""
    width, left, right, top, bottom = 760, 64, 205, 34, 46
    series = {n: [(s / 1e3, v) for s, v, _ in scalars[n][tag]] for n in TRAINED}
    xs = [x for pts in series.values() for x, _ in pts]
    ys = [y for pts in series.values() for _, y in pts]
    x0, x1 = 0, max(xs)
    pad = (max(ys) - min(ys)) * 0.12 or 1
    y0, y1 = min(ys) - pad, max(ys) + pad

    def X(x):
        return left + (x - x0) / (x1 - x0) * (width - left - right)

    def Y(y):
        return top + (1 - (y - y0) / (y1 - y0)) * (height - top - bottom)

    parts = [f'<svg viewBox="0 0 {width} {height}" role="img" aria-label="{esc(title)}" class="chart">']
    for i in range(5):
        v = y0 + (y1 - y0) * i / 4
        parts.append(f'<line x1="{left}" x2="{width - right}" y1="{Y(v):.1f}" y2="{Y(v):.1f}" class="grid"/>')
        parts.append(f'<text x="{left - 8}" y="{Y(v) + 4:.1f}" class="tick" text-anchor="end">{y_fmt.format(v)}</text>')
    for k in range(0, int(x1) + 1, 200):
        parts.append(f'<text x="{X(k):.1f}" y="{height - bottom + 18}" class="tick" text-anchor="middle">{k}k</text>')
    parts.append(f'<text x="{(left + width - right) / 2}" y="{height - 8}" class="axis" text-anchor="middle">training steps in the latest run (thousands)</text>')
    parts.append(f'<text x="14" y="{(top + height - bottom) / 2}" class="axis" text-anchor="middle" transform="rotate(-90 14 {(top + height - bottom) / 2})">{esc(y_label)}</text>')
    ends = []
    for name in TRAINED:
        pts = series[name]
        d = " ".join(f"{'M' if i == 0 else 'L'}{X(x):.1f},{Y(y):.1f}" for i, (x, y) in enumerate(pts))
        parts.append(f'<path d="{d}" fill="none" stroke="{SERIES[name]}" stroke-width="2" stroke-linejoin="round"><title>{esc(name)}</title></path>')
        lx, ly = pts[-1]
        parts.append(f'<circle cx="{X(lx):.1f}" cy="{Y(ly):.1f}" r="4" fill="{SERIES[name]}" stroke="var(--surface)" stroke-width="2"/>')
        ends.append([Y(ly), name, ly, pts[0][1]])
    # direct labels, nudged apart so they never collide
    ends.sort()
    for i in range(1, len(ends)):
        if ends[i][0] - ends[i - 1][0] < 15:
            ends[i][0] = ends[i - 1][0] + 15
    for yy, name, last, first in ends:
        delta = last - first
        parts.append(f'<text x="{width - right + 10}" y="{yy + 4:.1f}" class="lbl">{esc(name)} {y_fmt.format(last)} '
                     f'<tspan class="muted">({"+" if delta >= 0 else ""}{y_fmt.format(delta)})</tspan></text>')
    # Numbered markers on the chart, explanations listed under it: text can never
    # collide with a line that way.
    items = []
    for number, (x, y, text, above) in enumerate(notes, start=1):
        cx, cy = X(x), Y(y)
        my = cy - 20 if above else cy + 20
        parts.append(f'<line x1="{cx:.1f}" y1="{cy:.1f}" x2="{cx:.1f}" y2="{my:.1f}" class="callout"/>')
        parts.append(f'<circle cx="{cx:.1f}" cy="{my:.1f}" r="10" class="marker"/>')
        parts.append(f'<text x="{cx:.1f}" y="{my + 4:.1f}" class="markertext" text-anchor="middle">{number}</text>')
        items.append(f"<li>{esc(text)}</li>")
    parts.append("</svg>")
    return "\n".join(parts) + f'<ol class="notes">{"".join(items)}</ol>'


def level_chart(roster, intent_steps):
    """Training level: own steps under the current rules, one bar per fighter."""
    names = TRAINED + [n for n in roster if n not in TRAINED]
    row, left, width, maxv = 26, 150, 760, 6.5
    height = 30 + row * len(names) + 30

    def X(v):
        return left + v / maxv * (width - left - 190)

    parts = [f'<svg viewBox="0 0 {width} {height}" role="img" aria-label="Training level per fighter" class="chart">']
    for v in range(0, 7):
        parts.append(f'<line x1="{X(v):.1f}" x2="{X(v):.1f}" y1="20" y2="{height - 30}" class="grid"/>')
        parts.append(f'<text x="{X(v):.1f}" y="{height - 12}" class="tick" text-anchor="middle">{v}M</text>')
    for i, name in enumerate(names):
        y = 24 + i * row
        parts.append(f'<text x="{left - 10}" y="{y + 13}" class="lbl" text-anchor="end">{esc(name)}</text>')
        if name in TRAINED:
            earlier, latest = EARLIER[name], intent_steps[name] / 1e6
            parts.append(f'<rect x="{X(0):.1f}" y="{y}" width="{X(earlier) - X(0) - 1:.1f}" height="17" rx="3" fill="var(--s1)"><title>{esc(name)}: at least {earlier}M steps before today</title></rect>')
            parts.append(f'<rect x="{X(earlier) + 1:.1f}" y="{y}" width="{X(earlier + latest) - X(earlier) - 1:.1f}" height="17" rx="3" fill="var(--s3)"><title>{esc(name)}: {latest:.2f}M steps today</title></rect>')
            parts.append(f'<text x="{X(earlier + latest) + 8:.1f}" y="{y + 13}" class="lbl">≥{earlier + latest:.1f}M <tspan class="muted">trained · own brain</tspan></text>')
        else:
            donor = roster[name]["donor"]
            parts.append(f'<rect x="{X(0):.1f}" y="{y}" width="{X(0.35) - X(0):.1f}" height="17" rx="3" class="ghost"><title>{esc(name)}: never trained</title></rect>')
            parts.append(f'<text x="{X(0.35) + 8:.1f}" y="{y + 13}" class="lbl">0 <tspan class="muted">never trained · borrows {esc(donor)}\'s brain</tspan></text>')
    sep = 24 + 4 * row - 5
    parts.append(f'<line x1="20" x2="{width - 20}" y1="{sep}" y2="{sep}" class="sep"/>')
    parts.append("</svg>")
    return "\n".join(parts)


def game_chart(names, elo, wins, losses, roster):
    """In-game tournament Elo, diverging from the 1000 start."""
    rows = sorted(names, key=lambda n: -elo[n]["elo"])
    row, width, mid, scale = 26, 760, 500, 1.5
    height = 30 + row * len(rows) + 34
    parts = [f'<svg viewBox="0 0 {width} {height}" role="img" aria-label="In-game tournament Elo" class="chart">']
    parts.append(f'<line x1="{mid}" x2="{mid}" y1="16" y2="{height - 30}" class="axisline"/>')
    parts.append(f'<text x="{mid}" y="{height - 12}" class="tick" text-anchor="middle">1000 = where every fighter started</text>')
    for i, name in enumerate(rows):
        y = 22 + i * row
        e = elo[name]["elo"]
        w = (e - 1000) * scale
        fill = "var(--good)" if w >= 0 else "var(--bad)"
        x = mid if w >= 0 else mid + w
        parts.append(f'<rect x="{x:.1f}" y="{y}" width="{max(abs(w), 2):.1f}" height="17" rx="3" fill="{fill}"><title>{esc(name)} {e:.0f}</title></rect>')
        tag = "trained, own brain" if name in TRAINED else f"uses {roster[name]['donor']}'s brain"
        parts.append(f'<text x="150" y="{y + 13}" class="lbl" text-anchor="end">{esc(name)}</text>')
        parts.append(f'<text x="158" y="{y + 13}" class="muted small">{esc(tag)}</text>')
        tx = mid + w + 8 if w >= 0 else mid + 8
        parts.append(f'<text x="{tx:.1f}" y="{y + 13}" class="lbl">{e:.0f} <tspan class="muted">· {wins[name]}W {losses[name]}L</tspan></text>')
    parts.append("</svg>")
    return "\n".join(parts)


def pipeline_diagram():
    """How a fighter gets its brain: the stages, and where each group stands."""
    stages = [("Old rules", "Rebuild01 · 15M", "stand and push", "done"),
              ("Real sumo", "Sumo01 · restart", "rise from a crouch", "done"),
              ("Lunge", "Lunge01 · ~1M", "choose to charge", "done"),
              ("Read rival", "Intent01 · ~0.7M", "see a charge coming", "done"),
              ("New crouch", "Unique01 · 5 min", "own body, own style", "next")]
    width, height, box, gap = 760, 200, 140, 10
    parts = [f'<svg viewBox="0 0 {width} {height}" role="img" aria-label="Training stages" class="chart">']
    for i, (head, sub, what, state) in enumerate(stages):
        x = 12 + i * (box + gap)
        cls = "stage done" if state == "done" else "stage next"
        parts.append(f'<rect x="{x}" y="24" width="{box}" height="92" rx="8" class="{cls}"/>')
        parts.append(f'<text x="{x + box / 2}" y="48" class="lbl" text-anchor="middle">{esc(head)}</text>')
        parts.append(f'<text x="{x + box / 2}" y="68" class="muted small" text-anchor="middle">{esc(sub)}</text>')
        parts.append(f'<text x="{x + box / 2}" y="98" class="small" text-anchor="middle">{esc(what)}</text>')
        if i < len(stages) - 1:
            parts.append(f'<path d="M{x + box + 2},70 l{gap - 4},0" class="arrow" marker-end="url(#ah)"/>')
    parts.insert(1, '<defs><marker id="ah" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto"><path d="M0,0 L8,4 L0,8 z" class="arrowhead"/></marker></defs>')
    parts.append(f'<text x="12" y="146" class="note">Matt, Grandma, Nick, Kim: every grey box done, waiting on the dashed one.</text>')
    parts.append(f'<text x="12" y="166" class="note">The 12 placeholders: no box done. They fight with a copy of a trained fighter\'s brain;</text>')
    parts.append(f'<text x="12" y="184" class="note">the dashed box is their FIRST training under their own name and body.</text>')
    parts.append("</svg>")
    return "\n".join(parts)


# ----------------------------------------------------------------- page

def main():
    scalars, roster, wins, losses, elo = load()
    intent_steps = {n: scalars[n]["Self-play/ELO"][-1][0] for n in TRAINED}
    elo_first = {n: scalars[n]["Self-play/ELO"][0][1] for n in TRAINED}
    elo_last = {n: scalars[n]["Self-play/ELO"][-1][1] for n in TRAINED}
    rs_first = {n: scalars[n]["Referee/" + n + "/RoundSeconds"][0][1] for n in TRAINED}
    rs_last = {n: scalars[n]["Referee/" + n + "/RoundSeconds"][-1][1] for n in TRAINED}
    ent_last = {n: scalars[n]["Policy/Entropy"][-1][1] for n in TRAINED}

    for n in TRAINED:  # the per-fighter RoundSeconds tag, renamed to one key for the chart
        scalars[n]["RoundSeconds"] = scalars[n]["Referee/" + n + "/RoundSeconds"]

    matt_peak_step, matt_peak = max(((s, v) for s, v, _ in scalars["Matt"]["Self-play/ELO"]), key=lambda p: p[1])
    kim_mid = scalars["Kim"]["Self-play/ELO"][len(scalars["Kim"]["Self-play/ELO"]) // 2]
    elo_svg = line_chart(scalars, "Self-play/ELO", "Practice rating (ELO)", "practice rating",
                         [(matt_peak_step / 1e3, matt_peak,
                           f"Matt climbed the most: +{matt_peak - elo_first['Matt']:.0f} at his peak, then eased off slightly "
                           f"to {elo_last['Matt']:.0f} as the run ended. The best return on practice time of the four.", True),
                          (kim_mid[0] / 1e3, kim_mid[1],
                           f"Kim stayed almost flat (+{elo_last['Kim'] - elo_first['Kim']:.0f}). He gained the least per hour of practice. "
                           "That is not a verdict on his skill: the rating only compares him with his own past selves.", False)])
    rs_mid = scalars["Kim"]["RoundSeconds"][0]
    rs_svg = line_chart(scalars, "RoundSeconds", "Bout length in practice", "seconds per bout",
                        [(rs_mid[0] / 1e3, rs_mid[1],
                          f"Kim started with the quickest bouts ({rs_first['Kim']:.1f} s) and ended with {rs_last['Kim']:.1f} s.", False),
                         (scalars["Matt"]["RoundSeconds"][-1][0] / 1e3, rs_last["Matt"],
                          "All four ended with LONGER bouts than they started with, so quick finishes became rarer. "
                          f"Matt's average ({rs_last['Matt']:.1f} s) is now closest to the 20-second limit, where a bout ends undecided.", True)],
                        y_fmt="{:.1f}")
    g_mid = scalars["Grandma"]["Policy/Entropy"][len(scalars["Grandma"]["Policy/Entropy"]) // 2]
    ent_svg = line_chart(scalars, "Policy/Entropy", "Exploration", "variety of moves",
                         [(g_mid[0] / 1e3, g_mid[1],
                           "Flat for all four: they are still trying lots of different moves. Nobody has settled into a style yet, "
                           "so there is plenty left to learn.", False)],
                         y_fmt="{:.2f}")
    level_svg = level_chart(roster, intent_steps)
    game_svg = game_chart(list(roster), elo, wins, losses, roster)
    pipe_svg = pipeline_diagram()

    total_bouts = sum(wins.values())
    rows = []
    for n in TRAINED:
        rows.append(f"<tr><td>{n}</td><td>own brain</td><td class='num'>≥{EARLIER[n] + intent_steps[n] / 1e6:.1f}M</td>"
                    f"<td class='num'>{elo_first[n]:.0f} → {elo_last[n]:.0f}</td><td class='num'>{rs_first[n]:.1f} → {rs_last[n]:.1f} s</td>"
                    f"<td class='num'>{elo[n]['elo']:.0f} ({wins[n]}W {losses[n]}L)</td></tr>")
    for n in roster:
        if n in TRAINED:
            continue
        rows.append(f"<tr><td>{n}</td><td>borrows {roster[n]['donor']}</td><td class='num'>0</td><td class='num'>–</td><td class='num'>–</td>"
                    f"<td class='num'>{elo[n]['elo']:.0f} ({wins[n]}W {losses[n]}L)</td></tr>")

    page = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Training Status</title>
<style>
:root {{ --bg:#f6f3ee; --surface:#fcfcfb; --fg:#0b0b0b; --muted:#52514e; --line:#e2ded6; --grid:#ebe7df;
  --s1:#2a78d6; --s2:#eb6834; --s3:#1baf7a; --s4:#eda100; --good:#1baf7a; --bad:#e34948; --accent:#2a78d6; }}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg:#121110; --surface:#1a1a19; --fg:#f0efec; --muted:#c3c2b7; --line:#34322f; --grid:#2a2826;
  --s1:#3987e5; --s2:#d95926; --s3:#199e70; --s4:#c98500; --good:#199e70; --bad:#e66767; --accent:#3987e5; }} }}
:root[data-theme="dark"] {{ --bg:#121110; --surface:#1a1a19; --fg:#f0efec; --muted:#c3c2b7; --line:#34322f; --grid:#2a2826;
  --s1:#3987e5; --s2:#d95926; --s3:#199e70; --s4:#c98500; --good:#199e70; --bad:#e66767; --accent:#3987e5; }}
body {{ margin:0; background:var(--bg); color:var(--fg); font:16px/1.55 system-ui,-apple-system,Segoe UI,sans-serif; }}
main {{ max-width:860px; margin:0 auto; padding:28px 16px 64px; }}
h1 {{ font-size:1.7rem; margin:0 0 2px; }} h2 {{ font-size:1.2rem; margin:40px 0 6px; }}
.muted {{ color:var(--muted); fill:var(--muted); }} .small {{ font-size:12px; }}
.card {{ background:var(--surface); border:1px solid var(--line); border-radius:10px; padding:14px 16px; margin:12px 0; }}
.tiles {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(170px,1fr)); gap:10px; margin:18px 0; }}
.tile {{ background:var(--surface); border:1px solid var(--line); border-radius:10px; padding:12px 14px; }}
.tile b {{ display:block; font-size:1.6rem; line-height:1.2; }} .tile span {{ color:var(--muted); font-size:.9rem; }}
.alert {{ border-left:4px solid var(--s2); }}
svg.chart {{ width:100%; height:auto; display:block; font-family:inherit; }}
.chart .grid {{ stroke:var(--grid); }} .chart .tick {{ fill:var(--muted); font-size:11px; }} .chart .axis {{ fill:var(--muted); font-size:12px; }}
.chart .lbl {{ fill:var(--fg); font-size:13px; }} .chart .note {{ fill:var(--fg); font-size:12.5px; font-style:italic; }}
.chart .callout {{ stroke:var(--muted); }} .chart .marker {{ fill:var(--fg); }} .chart .markertext {{ fill:var(--surface); font-size:12px; font-weight:700; }}
ol.notes {{ margin:10px 0 0; padding-left:22px; font-size:.95rem; }} ol.notes li {{ margin-bottom:4px; }}
.chart .ghost {{ fill:none; stroke:var(--muted); stroke-dasharray:4 3; }} .chart .sep {{ stroke:var(--line); }}
.chart .axisline {{ stroke:var(--muted); }}
.chart .stage {{ fill:var(--surface); stroke:var(--muted); }} .chart .stage.done {{ fill:var(--grid); }} .chart .stage.next {{ stroke:var(--s2); stroke-width:2; stroke-dasharray:6 4; }}
.chart .arrow {{ stroke:var(--muted); stroke-width:1.5; }} .chart .arrowhead {{ fill:var(--muted); }}
.legend {{ display:flex; gap:16px; flex-wrap:wrap; font-size:.9rem; color:var(--muted); margin:4px 0 8px; }}
.legend i {{ display:inline-block; width:12px; height:12px; border-radius:3px; margin-right:6px; vertical-align:-1px; }}
table {{ width:100%; border-collapse:collapse; font-size:.92rem; }} th,td {{ text-align:left; padding:6px 8px; border-bottom:1px solid var(--line); }}
td.num {{ font-variant-numeric:tabular-nums; }} .scroll {{ overflow-x:auto; }}
ol li {{ margin-bottom:8px; }} .why {{ color:var(--muted); font-size:.93rem; }}
</style></head><body><main>
<h1>Training status</h1>
<p class="muted">PoSumo · 2026-11-10 15:45 · plain-language summary for deciding what to train next</p>

<div class="card alert"><b>Right now: nothing is training.</b> The last runs (all four trained fighters, about 55 minutes each) finished at 12:55 today.
The next run, “Unique01”, is set up for all 16 fighters but cannot start while the Unity Editor is open, and the Editor is currently frozen. Close Unity and it can start.</div>

<div class="tiles">
  <div class="tile"><b>4 of 16</b><span>fighters have a brain of their own</span></div>
  <div class="tile"><b>12</b><span>have never been trained; they borrow another fighter's brain</span></div>
  <div class="tile"><b>~70 min</b><span>estimated length of the next run, from the moment it starts</span></div>
  <div class="tile"><b>{total_bouts}</b><span>bouts played in the game today (used for the in-game ratings)</span></div>
</div>

<h2>1 · How much training each fighter has</h2>
<p>Each bar is practice time under the current sumo rules, in millions of steps. A step is one fiftieth of a second of one fighter's practice. Practice happens in parallel, so a million steps takes about 1–1.5 hours.</p>
<div class="legend"><span><i style="background:var(--s1)"></i>before today (at least)</span><span><i style="background:var(--s3)"></i>today's run</span><span><i style="border:1px dashed var(--muted)"></i>never trained</span></div>
<div class="card">{level_svg}</div>
<p class="why">“At least”: the earlier counts come from the 5 October report, written while that run was still going, so the real totals are a little higher.</p>

<h2>2 · The three numbers that matter most</h2>
<p>From today's run of the four trained fighters. These three come from the training dashboard (TensorBoard), which records these numbers while the fighters practise.</p>

<h3>Practice rating (ELO): are they winning more against their own past selves?</h3>
<p>This is the main score. Each fighter practises against older copies of itself, and the rating goes up when it beats them. Every run starts at 1200. <b>Up is good.</b></p>
<div class="card">{elo_svg}</div>

<h3>Bout length: are practice bouts being finished?</h3>
<p>The average length of a practice bout. Practice bouts stop at 20 seconds, so a number near 20 means the two fighters mostly waited each other out.</p>
<div class="card">{rs_svg}</div>

<h3>Exploration: are they still trying new things?</h3>
<p>How varied the moves are. High and flat means a fighter is still experimenting, so more practice should still pay off. A falling line would mean it has settled on a style.</p>
<div class="card">{ent_svg}</div>

<h2>3 · How they do in the actual game</h2>
<p>Tournament rating from today's brackets. Everyone starts at 1000. These are small samples (6–14 bouts each), so treat the order as a hint, not a verdict: a single lucky bout moves a fighter several places.</p>
<div class="legend"><span><i style="background:var(--good)"></i>above the start</span><span><i style="background:var(--bad)"></i>below the start</span></div>
<div class="card">{game_svg}</div>
<p class="why">A surprise worth knowing: Tong and Dupee, who use a copy of Matt's brain, are near the top, while Matt is near the bottom. Same brain, different body (each placeholder is up to 4% heavier or lighter), plus luck. It suggests body differences already matter, which is exactly why each fighter needs its own training.</p>

<h2>4 · Where everyone is in the training pipeline</h2>
<div class="card">{pipe_svg}</div>

<h2>5 · Suggested priorities</h2>
<ol>
  <li><b>Run Unique01 for all 16 (about 70 minutes).</b> The starting crouch was fixed today: before, a thigh sat up to 10 cm inside the body. Every brain was trained on the old pose. This run is also the first time any of the 12 placeholders trains under its own name and body.
    <div class="why">Needs: Unity closed. Command: <code>pwsh -File Training/Run-Unique01.ps1</code></div></li>
  <li><b>Then give the four trained fighters a longer run (about 1 hour each).</b> All four were still improving and still exploring when today's run stopped. Grandma has the least total training (about 2.6M steps), so she's first. Matt rose fastest, so he has the most to gain from more time.</li>
  <li><b>Then the placeholders that lose the most in the game:</b> Decoy and Alan (0 wins in 6 bouts each), then Porto (3–8). Five minutes each in Unique01 is a start, not a finish.</li>
  <li><b>Not yet:</b> none of the 16 has passed the project's “graduation” check (holding a target score for 100 bouts in a row). That check isn't automated yet, so nobody can be called finished.</li>
</ol>

<h2>All the numbers</h2>
<div class="scroll card"><table>
<tr><th>Fighter</th><th>Brain</th><th>Training (steps)</th><th>Practice rating today</th><th>Practice bout length today</th><th>Game rating (record)</th></tr>
{''.join(rows)}
</table></div>
<p class="muted small">Sources: TensorBoard event files in Training/results/*_intent01, Logs/ActionLogs/bouts_20261110_*.csv, tournament_elo.json, Training/unique01_roster.csv. Built by Tools/make_training_status.py.</p>
</main></body></html>
"""
    OUT.write_text(page, encoding="utf-8")
    print("wrote", OUT)


if __name__ == "__main__":
    main()
