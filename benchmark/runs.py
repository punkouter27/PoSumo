"""Training runs grid: Training/results/runs.html, one row per real training run under Training/results/, newest first.

    python benchmark/runs.py                                   rebuild Training/results/runs.html from the run folders
    python benchmark/runs.py --record <run-id> --ended "<how>" [--launcher <script>]
                                                               note this machine and how the run ended, then rebuild

The grid is rebuilt from what mlagents-learn leaves in each run folder (run_logs/timers.json: start, end, command
line; run_logs/training_status.json: checkpoint steps and times), so a run started by hand, or one whose launcher was
killed, still gets a row. Many PoSumo run folders no longer hold timers.json / configuration.yaml (only the behaviour
folder, run_logs/Player-N.log and STAMP.json survive); for those the row comes from the newest TensorBoard event file
(session start, host), the numbered <Brain>-<steps>.pt/.onnx checkpoints (steps, and their file times for speed),
the Player-N.log count (envs) and STAMP.json / Training/stamps/<run>.json (config, warm start).
What the folder cannot tell (which machine, which commit, how the run ended) comes from
Training/results/<run>/run_meta.json, which --record writes; Training/Update-RunsGrid.ps1 calls it when a run started
by Start-Training.ps1, Start-StaminaExtension.ps1 or Run-Unique01.ps1 ends, crash or not. Without it those cells
read "-".

Speed is the run's own: trainer steps per second of the busiest brain, measured between the first and last checkpoint
of this session, so a resumed run is not credited with the steps of its earlier sessions. Benchmark is the newest
benchmark/history.json result on the same machine from before the run started (ms per env step), for comparison.
Stdlib only; core.py next to this file supplies the machine description.
"""

from __future__ import annotations

import argparse
import datetime
import html
import json
import os
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
RESULTS = ROOT / "Training" / "results"
STAMPS = ROOT / "Training" / "stamps"  # scripts/preflight.py's per-run stamp, copied into the run as STAMP.json
OUT = RESULTS / "runs.html"
BENCH = HERE / "history.json"
META = "run_meta.json"
TITLE = "PoSumo"
SKIP = {"_benchmark"}  # the Benchmark's own throwaway run dir (benchmark/workload.py RUN_ID)
CKPT = re.compile(r"^(?P<brain>.+)-(?P<steps>\d+)\.(?:pt|onnx)$")
EVENTS = re.compile(r"events\.out\.tfevents\.(?P<t>\d+)\.(?P<host>[^.]+)\.")


# ---------------------------------------------------------------- collect


def _json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def _arg(cmd: str, name: str) -> str | None:
    m = re.search(rf"--{name}[= ](\S+)", cmd)
    return m.group(1) if m else None


def _speed(status: dict, start: float, end: float) -> tuple[str | None, int | None, float | None]:
    """(brain, steps reached, steps/s this session) for the brain with the most steps."""
    best = (None, None, None)
    for brain, s in status.items():
        if not isinstance(s, dict) or "checkpoints" not in s:
            continue
        cps = [c for c in s["checkpoints"] if c.get("steps") is not None]
        if s.get("final_checkpoint"):
            cps.append(s["final_checkpoint"])
        if not cps:
            continue
        reached = max(c["steps"] for c in cps)
        session = sorted(
            (c for c in cps if start - 1 <= (c.get("creation_time") or 0) <= end + 60), key=lambda c: c["creation_time"]
        )
        rate = None
        if len(session) >= 2 and session[-1]["creation_time"] > session[0]["creation_time"]:
            rate = (session[-1]["steps"] - session[0]["steps"]) / (
                session[-1]["creation_time"] - session[0]["creation_time"]
            )
        if best[1] is None or reached > best[1]:
            best = (brain, reached, rate)
    return best


def _bench_for(host: str | None, before: float) -> dict | None:
    runs = [r for r in _json(BENCH).get("runs", []) if r.get("ok") and r.get("result")]
    if host:
        runs = [r for r in runs if r.get("machine", {}).get("hostname") == host]
    if not runs:
        return None
    stamp = lambda r: datetime.datetime.fromisoformat(r["when"]).timestamp()  # noqa: E731
    earlier = [r for r in runs if stamp(r) <= before]
    return (earlier or runs)[-1]


def _warm_run(path: str) -> str:
    """The run folder an init_path points into (Training/results/<run>/<Brain>/checkpoint.pt -> <run>, either slash)."""
    parts = [p for p in re.split(r"[\\/]", path) if p]
    if "results" in parts and parts.index("results") + 1 < len(parts):
        return parts[parts.index("results") + 1]
    return parts[0] if parts else path


def _stamp(d: Path) -> dict:
    return _json(d / "STAMP.json") or _json(STAMPS / f"{d.name}.json")


def _from_files(d: Path) -> dict | None:
    """Session facts for a run folder without run_logs/timers.json, from the files every session leaves behind.

    Start and host come from the busiest brain's newest TensorBoard event file (one per session), steps from its
    numbered checkpoints, speed from the checkpoint file times inside that session. None when no brain has events.
    """
    best = None
    for b in d.iterdir():
        if not b.is_dir() or b.name == "run_logs":
            continue
        events = sorted(
            (int(m["t"]), m["host"], f) for f in b.glob("events.out.tfevents.*") if (m := EVENTS.search(f.name))
        )
        if not events:
            continue
        start, host, latest = events[-1]
        cps: dict[int, float] = {}
        for f in b.iterdir():
            m = CKPT.match(f.name)
            if m and m["brain"] == b.name:
                steps = int(m["steps"])
                cps[steps] = min(cps.get(steps, f.stat().st_mtime), f.stat().st_mtime)
        session = sorted((t, s) for s, t in cps.items() if t >= start - 1)
        end = max([latest.stat().st_mtime] + [t for t, _ in session])
        rate = None
        if len(session) >= 2 and session[-1][0] > session[0][0]:
            rate = (session[-1][1] - session[0][1]) / (session[-1][0] - session[0][0])
        reached = max(cps) if cps else None
        if best is None or (reached or 0) > (best["steps"] or 0):
            best = dict(
                start=float(start), end=end, brain=b.name, steps=reached, rate=rate, resumed=len(events) > 1, host=host
            )
    return best


def collect() -> list[dict]:
    rows = []
    if not RESULTS.is_dir():
        return rows
    for d in RESULTS.iterdir():
        if d.name in SKIP or not d.is_dir():
            continue
        timers = d / "run_logs" / "timers.json"
        meta = _json(d / META)
        stamp = _stamp(d)
        players = len(list((d / "run_logs").glob("Player-*.log")))
        if timers.exists():
            t = _json(timers)
            md = t.get("metadata", {})
            cmd = md.get("command_line_arguments", "")
            start = float(md.get("start_time_seconds") or timers.stat().st_mtime)
            end = float(md["end_time_seconds"]) if md.get("end_time_seconds") else timers.stat().st_mtime
            brain, steps, rate = _speed(_json(d / "run_logs" / "training_status.json"), start, end)
            files = _from_files(d) or {}
            resumed = "--resume" in cmd
            init_from = _arg(cmd, "initialize-from")
            cfg = next((a for a in cmd.split()[1:] if a.endswith((".yaml", ".yml"))), None)
            envs = _arg(cmd, "num-envs") or "1 (Editor)"
            ended = "still running or killed" if not md.get("end_time_seconds") else "exited"
            host = files.get("host")
        else:
            files = _from_files(d)
            if not files:  # a warm-start staging folder (checkpoint only) or an empty dir: not a training run
                continue
            start, end, brain, steps, rate = (files[k] for k in ("start", "end", "brain", "steps", "rate"))
            resumed = files["resumed"]
            init_from = stamp.get("initialize_from")
            cfg = stamp.get("config")
            envs = str(players) if players else "1 (Editor)"
            ended = "not recorded"
            host = files["host"]
        config_text = (
            (d / "configuration.yaml").read_text(encoding="utf-8", errors="replace")
            if (d / "configuration.yaml").exists()
            else ""
        )
        warm = sorted(
            {_warm_run(p) for p in re.findall(r"init_path:\s*(\S+)", config_text) if p not in ("null", "~")}
            - {init_from}
        )
        warm = ([init_from] if init_from else []) + warm
        # A warm start inherits the source's step count, so steps / wall-clock is only this session's rate when cold.
        if rate is None and steps and not resumed and not warm and end > start:
            rate = steps / (end - start)
        host = meta.get("host") or host
        rows.append(
            dict(
                run=d.name,
                start=start,
                wall=max(0.0, end - start),
                ended=meta.get("ended") or ended,
                resumed=resumed,
                brain=brain,
                steps=steps,
                rate=rate,
                bench=_bench_for(host, start),
                config=Path(cfg.replace("\\", "/")).name if cfg else None,
                envs=envs,
                warm=warm,
                commit=meta.get("commit"),
                host=host,
                gpu=meta.get("gpu"),
                device=meta.get("device"),
                launcher=meta.get("launcher"),
            )
        )
    rows.sort(key=lambda r: r["start"], reverse=True)
    return rows


# ---------------------------------------------------------------- render


def _dur(s: float) -> str:
    h, m = divmod(int(s) // 60, 60)
    return f"{h} h {m:02d} m" if h else f"{m} m"


def _num(n: float | None, digits: int = 0) -> str:
    return "-" if n is None else f"{n:,.{digits}f}"


def render(rows: list[dict]) -> str:
    e = html.escape
    body = []
    for r in rows:
        b = r["bench"]
        if b:
            notes = [f"{b['result']['env_steps_per_s']} env steps/s", e(b["when"][:10])]
            if datetime.datetime.fromisoformat(b["when"]).timestamp() > r["start"]:
                notes.append("taken after the run")
            if not r["host"]:
                notes.append("machine unknown")
            bench = f"{b['result']['step_time_ms']} ms<small>{' · '.join(notes)}</small>"
        else:
            bench = "-<small>no benchmark on this machine</small>"
        ended = e(r["ended"])
        cls = "bad" if re.search(r"crash|died|killed|HALT|error|exception", r["ended"], re.I) else ""
        body.append(
            "<tr>"
            f"<td><b>{e(r['run'])}</b>{'<small>resumed</small>' if r['resumed'] else ''}</td>"
            f"<td>{datetime.datetime.fromtimestamp(r['start']).strftime('%Y-%m-%d %H:%M')}</td>"
            f'<td class="n">{_dur(r["wall"])}</td>'
            f'<td class="{cls}">{ended}</td>'
            f'<td class="n">{_num(r["steps"])}<small>{e(r["brain"] or "")}</small></td>'
            f'<td class="n"><b>{_num(r["rate"])}</b><small>trainer steps/s</small></td>'
            f'<td class="n">{bench}</td>'
            f"<td>{e(r['config'] or '-')}</td>"
            f'<td class="n">{e(str(r["envs"]))}</td>'
            f"<td>{e(', '.join(r['warm']) or 'cold')}</td>"
            f'<td class="mono">{e(r["commit"] or "-")}</td>'
            f"<td>{e(r['host'] or '-')}<small>{e(r['gpu'] or '')}{' · ' + e(r['device']) if r['device'] else ''}"
            f"</small></td>"
            "</tr>"
        )
    latest = rows[0] if rows else None
    prev = next((r for r in rows[1:] if r["rate"]), None) if rows else None
    delta = ""
    if latest and latest["rate"] and prev:
        pct = 100 * (latest["rate"] / prev["rate"] - 1)
        delta = f'<span class="{"up" if pct >= 0 else "down"}">{pct:+.0f}% vs {e(prev["run"])}</span>'
    summary = (
        f'<div class="sum"><div><span>Latest run</span><b>{e(latest["run"])}</b></div>'
        f"<div><span>Speed</span><b>{_num(latest['rate'])} steps/s</b>{delta}</div>"
        f"<div><span>Wall-clock</span><b>{_dur(latest['wall'])}</b></div>"
        f"<div><span>Runs listed</span><b>{len(rows)}</b></div></div>"
        if latest
        else "<p>No training runs under Training/results/ yet.</p>"
    )
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")
    return f"""<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><title>{e(TITLE)} training runs</title>
<style>
:root{{--bg:#f5f7f6;--card:#fff;--ink:#18211f;--muted:#5d6b67;--line:#d8e0dd;--accent:#0f6b5c;--bad:#b42318;--up:#1f7a43}}
@media (prefers-color-scheme:dark){{:root{{--bg:#0f1514;--card:#161e1c;--ink:#e2eae7;--muted:#91a19c;--line:#27332f;
--accent:#5cc9b0;--bad:#ff8a7a;--up:#5fcf8a;color-scheme:dark}}}}
body{{margin:0;background:var(--bg);color:var(--ink);font:14px/1.5 "Segoe UI",system-ui,sans-serif;padding:24px 16px}}
h1{{font-size:20px;margin:0 0 4px}} p{{margin:0;color:var(--muted)}} .wrap{{max-width:1500px;margin:auto;display:grid;gap:16px}}
.sum{{display:flex;flex-wrap:wrap;gap:12px 40px;background:var(--card);border:1px solid var(--line);border-radius:8px;padding:14px 18px}}
.sum span{{display:block;color:var(--muted);font-size:12px}} .sum b{{font-size:20px}} .sum .up,.sum .down{{display:block;font-size:12px}}
.up{{color:var(--up)}} .down{{color:var(--bad)}}
.t{{overflow-x:auto;background:var(--card);border:1px solid var(--line);border-radius:8px}}
table{{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}}
th,td{{padding:8px 12px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top;white-space:nowrap}}
th{{font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--muted);position:sticky;top:0;background:var(--card)}}
tr:last-child td{{border-bottom:0}} td.n{{text-align:right}} td small{{display:block;color:var(--muted);font-size:11px}}
td.bad{{color:var(--bad)}} .mono{{font-family:Consolas,monospace}}
</style></head><body><div class="wrap">
<div><h1>{e(TITLE)} training runs</h1><p>Every real training run under Training/results/, newest first. Rebuilt {now} by
benchmark/runs.py. Speed = trainer steps/s of the busiest brain this session; Benchmark = the newest benchmark.ps1
result on the same machine before the run (ms per env step).</p></div>
{summary}
<div class="t"><table><thead><tr><th>Run</th><th>Started</th><th>Wall-clock</th><th>Ended</th><th>Steps</th>
<th>Speed</th><th>Benchmark</th><th>Config</th><th>Envs</th><th>Warm start</th><th>Commit</th><th>Machine</th></tr></thead>
<tbody>{"".join(body)}</tbody></table></div></div></body></html>
"""


def build() -> Path:
    RESULTS.mkdir(parents=True, exist_ok=True)
    tmp = OUT.with_suffix(".tmp")
    tmp.write_text(render(collect()), encoding="utf-8")
    os.replace(tmp, OUT)
    return OUT


def record(run_id: str, ended: str, launcher: str | None) -> None:
    """Write Training/results/<run>/run_meta.json: this machine, the commit and how the run ended."""
    sys.path.insert(0, str(HERE))
    import core

    m = core.machine()
    d = RESULTS / run_id
    d.mkdir(parents=True, exist_ok=True)
    meta = dict(
        host=m["hostname"],
        gpu=m["gpu"],
        cpu=m["cpu"],
        device="CPU" if os.environ.get("CUDA_VISIBLE_DEVICES") == "-1" else None,
        commit=core.git_commit(),
        ended=ended,
        launcher=launcher,
        recorded=datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
    )
    (d / META).write_text(json.dumps(meta, indent=1), encoding="utf-8")


def main() -> None:
    p = argparse.ArgumentParser(description="Rebuild Training/results/runs.html (the training runs grid).")
    p.add_argument("--record", metavar="RUN_ID", help="note this machine and how the run ended first")
    p.add_argument("--ended", default="exited", help="how the run ended, in words")
    p.add_argument("--launcher", help="the script that ran it")
    a = p.parse_args()
    if a.record:
        record(a.record, a.ended, a.launcher)
    print(f"Training runs grid: {build()}")


if __name__ == "__main__":
    main()
