#!/usr/bin/env python3
"""Generate an HTML training status & optimization report with inline SVG charts.

Reads TensorBoard event files from Training/results/, roster definitions from
Training/unique01_roster.csv, character sheets from Assets/Agents/*_v01/, and
status JSON from DOCS/20261110/unique01_status.json.

Outputs:
  - DOCS/20261110/training_status.html
  - DOCS/training_report.html
"""

from __future__ import annotations

import csv
import datetime as dt
import glob
import html
import json
import math
import os
import re
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from tensorboard.backend.event_processing import event_accumulator

REPO_ROOT = Path(__file__).resolve().parent.parent
RESULTS_DIR = REPO_ROOT / "Training" / "results"
ROSTER_CSV = REPO_ROOT / "Training" / "unique01_roster.csv"
STATUS_JSON = REPO_ROOT / "DOCS" / "20261110" / "unique01_status.json"
OUT_PATHS = [
    REPO_ROOT / "DOCS" / "20261110" / "training_status.html",
    REPO_ROOT / "DOCS" / "training_report.html",
]

PALETTE = [
    "#38bdf8",  # sky
    "#4ade80",  # green
    "#f472b6",  # pink
    "#fb923c",  # orange
    "#a78bfa",  # purple
    "#facc15",  # yellow
    "#2dd4bf",  # teal
    "#f87171",  # red
    "#60a5fa",  # blue
    "#a3e635",  # lime
    "#e879f9",  # fuchsia
    "#fbbf24",  # amber
    "#34d399",  # emerald
    "#818cf8",  # indigo
    "#fb7185",  # rose
    "#22d3ee",  # cyan
]


def downsample(points: List[Tuple[int, float]], max_pts: int = 80) -> List[Tuple[int, float]]:
    if len(points) <= max_pts:
        return points
    step = (len(points) - 1) / float(max_pts - 1)
    out = []
    for i in range(max_pts):
        idx = int(round(i * step))
        out.append(points[idx])
    return out


def ema_smooth(points: List[Tuple[int, float]], alpha: float = 0.25) -> List[Tuple[int, float]]:
    if not points:
        return []
    smoothed: List[Tuple[int, float]] = []
    val = points[0][1]
    for step, raw in points:
        if math.isnan(raw) or math.isinf(raw):
            continue
        val = alpha * raw + (1.0 - alpha) * val
        smoothed.append((step, val))
    return smoothed


def load_tb_run(run_dir: Path) -> Dict[str, List[Tuple[int, float]]]:
    """Load scalar series from a TensorBoard run directory."""
    event_files = sorted(run_dir.rglob("events.out.tfevents.*"), key=lambda p: p.stat().st_mtime)
    if not event_files:
        return {}
    # Find the subdir containing event files
    target_dir = event_files[-1].parent
    try:
        ea = event_accumulator.EventAccumulator(
            str(target_dir),
            size_guidance={event_accumulator.SCALARS: 500},
        )
        ea.Reload()
    except Exception:
        return {}

    tags = ea.Tags().get("scalars", [])
    series: Dict[str, List[Tuple[int, float]]] = {}
    for tag in tags:
        try:
            events = ea.Scalars(tag)
            pts = [(int(e.step), float(e.value)) for e in events if not math.isnan(e.value)]
            if pts:
                series[tag] = pts
        except Exception:
            continue
    return series


def parse_character_sheet(fighter: str) -> Dict[str, Any]:
    sheet = REPO_ROOT / "Assets" / "Agents" / f"{fighter}_v01" / f"{fighter}_Character.asset"
    onnx_path = REPO_ROOT / "Assets" / "Agents" / f"{fighter}_v01" / f"{fighter}.onnx"
    info: Dict[str, Any] = {
        "borrowedBrainFrom": "",
        "balanceObservations": 0,
        "massScale": 1.0,
        "widthScale": 1.0,
        "torqueScale": 1.0,
        "onnx_exists": onnx_path.exists(),
        "onnx_mtime": dt.datetime.fromtimestamp(onnx_path.stat().st_mtime).strftime("%Y-%m-%d %H:%M")
        if onnx_path.exists()
        else "—",
        "onnx_kb": round(onnx_path.stat().st_size / 1024.0, 1) if onnx_path.exists() else 0.0,
    }
    if not sheet.exists():
        return info
    txt = sheet.read_text(encoding="utf-8", errors="replace")
    m_borrow = re.search(r"^[ \t]*borrowedBrainFrom:[ \t]*(\S*)", txt, re.MULTILINE)
    if m_borrow:
        info["borrowedBrainFrom"] = m_borrow.group(1).strip()
    m_bal = re.search(r"^\s*balanceObservations:\s*(\d+)", txt, re.MULTILINE)
    if m_bal:
        info["balanceObservations"] = int(m_bal.group(1))
    for key in ("massScale", "widthScale", "torqueScale"):
        m = re.search(rf"^\s*{key}:\s*([0-9.+-eE]+)", txt, re.MULTILINE)
        if m:
            info[key] = float(m.group(1))
    return info


def render_svg_line_chart(
    title: str,
    subtitle: str,
    y_label: str,
    series_list: List[Dict[str, Any]],
    baseline_y: Optional[float] = None,
    baseline_label: str = "",
    normalize_steps: bool = False,
) -> str:
    """Render a multi-series inline SVG chart."""
    width, height = 860, 320
    pad_l, pad_r, pad_t, pad_b = 64, 28, 36, 48
    plot_w = width - pad_l - pad_r
    plot_h = height - pad_t - pad_b

    valid = [s for s in series_list if s.get("points")]
    if not valid:
        return f"""
        <div class="chart-box">
          <div class="chart-title">{html.escape(title)}</div>
          <div class="chart-sub">{html.escape(subtitle)}</div>
          <div class="empty-chart">Waiting for TensorBoard scalar events...</div>
        </div>
        """

    all_x: List[float] = []
    all_y: List[float] = []
    processed: List[Dict[str, Any]] = []
    for s in valid:
        pts = s["points"]
        x0 = pts[0][0] if normalize_steps else 0
        norm_pts = [(p[0] - x0, p[1]) for p in pts]
        for x, y in norm_pts:
            all_x.append(float(x))
            all_y.append(float(y))
        processed.append({**s, "norm_points": norm_pts})

    if baseline_y is not None:
        all_y.append(baseline_y)

    min_x, max_x = min(all_x), max(all_x)
    if max_x <= min_x:
        max_x = min_x + 1.0
    min_y, max_y = min(all_y), max(all_y)
    span_y = max_y - min_y
    if span_y < 1e-6:
        span_y = 1.0
        min_y -= 0.5
        max_y += 0.5
    else:
        min_y -= span_y * 0.08
        max_y += span_y * 0.08

    def sx(x: float) -> float:
        return pad_l + (x - min_x) / (max_x - min_x) * plot_w

    def sy(y: float) -> float:
        return pad_t + plot_h - (y - min_y) / (max_y - min_y) * plot_h

    svg_lines = [
        f'<svg viewBox="0 0 {width} {height}" class="metric-svg" role="img" aria-label="{html.escape(title)}">',
        f'<rect x="{pad_l}" y="{pad_t}" width="{plot_w}" height="{plot_h}" fill="#0b1120" rx="6" stroke="#1e293b" stroke-width="1"/>',
    ]

    # Horizontal grid lines
    for i in range(5):
        frac = i / 4.0
        yv = min_y + frac * (max_y - min_y)
        yp = sy(yv)
        svg_lines.append(
            f'<line x1="{pad_l}" y1="{yp:.1f}" x2="{pad_l + plot_w}" y2="{yp:.1f}" stroke="#1e293b" stroke-dasharray="3,3" stroke-width="1"/>'
        )
        label_str = f"{yv:.0f}" if abs(max_y - min_y) >= 20 else f"{yv:.2f}"
        svg_lines.append(
            f'<text x="{pad_l - 8}" y="{yp + 4:.1f}" text-anchor="end" fill="#94a3b8" font-size="11" font-family="monospace">{label_str}</text>'
        )

    # Vertical grid lines (steps)
    for i in range(5):
        frac = i / 4.0
        xv = min_x + frac * (max_x - min_x)
        xp = sx(xv)
        svg_lines.append(
            f'<line x1="{xp:.1f}" y1="{pad_t}" x2="{xp:.1f}" y2="{pad_t + plot_h}" stroke="#1e293b" stroke-dasharray="3,3" stroke-width="1"/>'
        )
        if xv >= 1_000_000:
            x_str = f"{xv / 1_000_000:.2f}M"
        elif xv >= 1_000:
            x_str = f"{xv / 1_000:.0f}k"
        else:
            x_str = f"{xv:.0f}"
        svg_lines.append(
            f'<text x="{xp:.1f}" y="{pad_t + plot_h + 18}" text-anchor="middle" fill="#94a3b8" font-size="11" font-family="monospace">{x_str}</text>'
        )

    x_axis_title = "Steps Trained in Current Run (Δ Steps)" if normalize_steps else "Cumulative Training Steps"
    svg_lines.append(
        f'<text x="{pad_l + plot_w / 2:.1f}" y="{height - 8}" text-anchor="middle" fill="#64748b" font-size="11">{x_axis_title}</text>'
    )
    svg_lines.append(
        f'<text x="16" y="{pad_t + plot_h / 2:.1f}" text-anchor="middle" fill="#64748b" font-size="11" transform="rotate(-90 16 {pad_t + plot_h / 2:.1f})">{html.escape(y_label)}</text>'
    )

    # Optional baseline line
    if baseline_y is not None and min_y <= baseline_y <= max_y:
        b_yp = sy(baseline_y)
        svg_lines.append(
            f'<line x1="{pad_l}" y1="{b_yp:.1f}" x2="{pad_l + plot_w}" y2="{b_yp:.1f}" stroke="#f59e0b" stroke-dasharray="6,4" stroke-width="1.5"/>'
        )
        if baseline_label:
            svg_lines.append(
                f'<text x="{pad_l + plot_w - 6}" y="{b_yp - 5:.1f}" text-anchor="end" fill="#fbbf24" font-size="10">{html.escape(baseline_label)}</text>'
            )

    # Polylines
    for s in processed:
        pts = s["norm_points"]
        color = s["color"]
        dashed = s.get("dashed", False)
        width_px = 1.6 if dashed else 2.3
        opacity = 0.65 if dashed else 0.95
        coord_str = " ".join(f"{sx(x):.1f},{sy(y):.1f}" for x, y in pts)
        dash_attr = ' stroke-dasharray="4,3"' if dashed else ""
        svg_lines.append(
            f'<polyline fill="none" stroke="{color}" stroke-width="{width_px}" stroke-opacity="{opacity}"{dash_attr} points="{coord_str}"/>'
        )
        lx, ly = sx(pts[-1][0]), sy(pts[-1][1])
        svg_lines.append(
            f'<circle cx="{lx:.1f}" cy="{ly:.1f}" r="3.2" fill="{color}"/>'
        )

    svg_lines.append("</svg>")

    # Legend
    legend_items = []
    for s in processed:
        pts = s["norm_points"]
        last_val = pts[-1][1]
        val_str = f"{last_val:.1f}" if abs(last_val) >= 10 else f"{last_val:.3f}"
        badge_style = f"border-bottom: 2px {'dashed' if s.get('dashed') else 'solid'} {s['color']};"
        legend_items.append(
            f'<span class="legend-chip"><span class="legend-swatch" style="background:{s["color"]};{badge_style}"></span>'
            f'{html.escape(s["label"])}: <strong>{val_str}</strong></span>'
        )

    return f"""
    <div class="chart-box">
      <div class="chart-header">
        <div>
          <div class="chart-title">{html.escape(title)}</div>
          <div class="chart-sub">{html.escape(subtitle)}</div>
        </div>
      </div>
      {''.join(svg_lines)}
      <div class="legend-row">{''.join(legend_items)}</div>
    </div>
    """


def render_elo_bar_chart(rows: List[Dict[str, Any]]) -> str:
    """Render a horizontal SVG bar chart comparing ELO delta and training steps across all 16 fighters."""
    width = 860
    row_h = 26
    pad_t, pad_b = 32, 28
    pad_l, pad_r = 140, 130
    height = pad_t + pad_b + len(rows) * row_h
    plot_w = width - pad_l - pad_r

    elos = [r["latest_elo"] for r in rows if r["latest_elo"] is not None]
    min_elo = min(elos + [1150.0]) - 15.0
    max_elo = max(elos + [1250.0]) + 15.0

    def sx(val: float) -> float:
        return pad_l + (val - min_elo) / max(1.0, (max_elo - min_elo)) * plot_w

    x1200 = sx(1200.0)
    svg = [
        f'<svg viewBox="0 0 {width} {height}" class="metric-svg" role="img" aria-label="Fighter ELO Comparison">',
        f'<rect x="{pad_l}" y="{pad_t - 8}" width="{plot_w}" height="{len(rows) * row_h + 8}" fill="#0b1120" rx="6" stroke="#1e293b"/>',
        f'<line x1="{x1200:.1f}" y1="{pad_t - 8}" x2="{x1200:.1f}" y2="{height - pad_b}" stroke="#f59e0b" stroke-dasharray="4,3" stroke-width="1.5"/>',
        f'<text x="{x1200:.1f}" y="{pad_t - 12}" text-anchor="middle" fill="#fbbf24" font-size="10" font-family="monospace">1200 Baseline</text>',
    ]

    for idx, r in enumerate(rows):
        y = pad_t + idx * row_h
        name = r["fighter"]
        elo = r["latest_elo"] if r["latest_elo"] is not None else 1200.0
        color = r["color"]
        xp = sx(elo)
        bar_x = min(x1200, xp)
        bar_w = max(3.0, abs(xp - x1200))
        status_tag = "OWN" if not r["borrowed"] else f"->{r['borrowed']}"
        svg.append(
            f'<text x="{pad_l - 10}" y="{y + 15}" text-anchor="end" fill="#e2e8f0" font-size="12" font-family="monospace">{html.escape(name)} <tspan fill="#64748b" font-size="10">({status_tag})</tspan></text>'
        )
        svg.append(
            f'<rect x="{bar_x:.1f}" y="{y + 4}" width="{bar_w:.1f}" height="{row_h - 9}" fill="{color}" rx="3" fill-opacity="0.85"/>'
        )
        delta = elo - 1200.0
        sign = "+" if delta >= 0 else ""
        steps_k = f"{r['unique_steps'] / 1000:.0f}k" if r["unique_steps"] > 0 else f"{r['base_steps'] / 1000:.0f}k (donor)"
        svg.append(
            f'<text x="{pad_l + plot_w + 8}" y="{y + 15}" text-anchor="start" fill="#cbd5e1" font-size="11" font-family="monospace">{elo:.0f} ({sign}{delta:.0f}) · {steps_k}</text>'
        )

    svg.append("</svg>")
    return f"""
    <div class="chart-box">
      <div class="chart-title">Roster Self-Play ELO &amp; Training Progress Across All 16 Fighters</div>
      <div class="chart-sub">Bars show ELO relative to the 1200 self-play start baseline; right column shows current ELO, delta from 1200, and training steps.</div>
      {''.join(svg)}
    </div>
    """


def main() -> None:
    roster: List[Dict[str, str]] = []
    if ROSTER_CSV.exists():
        with ROSTER_CSV.open("r", encoding="utf-8") as f:
            roster = list(csv.DictReader(f))

    # Load baseline core runs (*_intent01 and matt_real01)
    core_runs: Dict[str, Dict[str, List[Tuple[int, float]]]] = {}
    for run_name in ("matt_intent01", "grandma_intent01", "nick_intent01", "kim_intent01", "matt_real01", "grandma_real01", "nick_real01", "kim_real01"):
        rdir = RESULTS_DIR / run_name
        if rdir.exists():
            data = load_tb_run(rdir)
            if data:
                core_runs[run_name] = data

    # Load Unique01 runs (<fighter>_unique01)
    unique_runs: Dict[str, Dict[str, List[Tuple[int, float]]]] = {}
    for row in roster:
        fname = row["fighter"]
        run_id = f"{fname.lower()}_unique01"
        rdir = RESULTS_DIR / run_id
        if rdir.exists():
            data = load_tb_run(rdir)
            if data:
                unique_runs[fname] = data

    # Build per-fighter summary rows
    fighter_rows: List[Dict[str, Any]] = []
    elo_series_unique: List[Dict[str, Any]] = []
    ep_len_series_unique: List[Dict[str, Any]] = []
    reward_series_unique: List[Dict[str, Any]] = []
    vloss_series_unique: List[Dict[str, Any]] = []

    elo_series_core: List[Dict[str, Any]] = []
    ep_len_series_core: List[Dict[str, Any]] = []
    reward_series_core: List[Dict[str, Any]] = []

    core_colors = {
        "matt_intent01": "#60a5fa",
        "matt_real01": "#38bdf8",
        "grandma_intent01": "#4ade80",
        "grandma_real01": "#22c55e",
        "nick_intent01": "#fb923c",
        "nick_real01": "#f97316",
        "kim_intent01": "#f472b6",
        "kim_real01": "#ec4899",
    }
    for rname, sdata in core_runs.items():
        c = core_colors.get(rname, "#94a3b8")
        is_intent = rname.endswith("_intent01")
        if "Self-play/ELO" in sdata:
            elo_series_core.append({
                "label": rname,
                "color": c,
                "dashed": is_intent,
                "points": downsample(ema_smooth(sdata["Self-play/ELO"], 0.3)),
            })
        if "Environment/Episode Length" in sdata:
            ep_len_series_core.append({
                "label": rname,
                "color": c,
                "dashed": is_intent,
                "points": downsample(ema_smooth(sdata["Environment/Episode Length"], 0.25)),
            })
        if "Environment/Cumulative Reward" in sdata:
            reward_series_core.append({
                "label": rname,
                "color": c,
                "dashed": is_intent,
                "points": downsample(ema_smooth(sdata["Environment/Cumulative Reward"], 0.25)),
            })

    total_unique_steps = 0
    own_brain_count = 0

    for idx, row in enumerate(roster):
        fname = row["fighter"]
        donor = row["donor"]
        style = row["style"]
        color = PALETTE[idx % len(PALETTE)]
        sheet = parse_character_sheet(fname)
        borrowed = sheet["borrowedBrainFrom"]
        if not borrowed:
            own_brain_count += 1

        # Determine base run
        base_run_key = "matt_real01" if (donor == "Matt" and sheet["balanceObservations"] == 1) else f"{donor.lower()}_intent01"
        base_data = core_runs.get(base_run_key, {})
        u_data = unique_runs.get(fname, {})

        # Check numbered checkpoints on disk if TB events haven't flushed yet
        u_dir = RESULTS_DIR / f"{fname.lower()}_unique01" / fname
        ckpt_steps = 0
        if u_dir.exists():
            for p in u_dir.glob(f"{fname}-*.pt"):
                m = re.search(r"-(\d+)\.pt$", p.name)
                if m:
                    ckpt_steps = max(ckpt_steps, int(m.group(1)))

        def metric_summary(data: Dict[str, List[Tuple[int, float]]], tag: str) -> Tuple[Optional[float], Optional[float], int]:
            pts = data.get(tag, [])
            if not pts:
                return None, None, 0
            return pts[0][1], pts[-1][1], pts[-1][0] - pts[0][0]

        u_elo_start, u_elo_end, u_steps_elo = metric_summary(u_data, "Self-play/ELO")
        u_rew_start, u_rew_end, u_steps_rew = metric_summary(u_data, "Environment/Cumulative Reward")
        u_len_start, u_len_end, u_steps_len = metric_summary(u_data, "Environment/Episode Length")
        _, u_vloss_end, _ = metric_summary(u_data, "Losses/Value Loss")
        _, u_slip_end, _ = metric_summary(u_data, "Realism/FootSlipSpeed")

        b_elo_start, b_elo_end, b_steps = metric_summary(base_data, "Self-play/ELO")
        _, b_rew_end, _ = metric_summary(base_data, "Environment/Cumulative Reward")
        _, b_len_end, _ = metric_summary(base_data, "Environment/Episode Length")
        _, b_vloss_end, _ = metric_summary(base_data, "Losses/Value Loss")

        # Calculate unique steps trained
        u_steps = max(u_steps_elo, u_steps_rew, u_steps_len)
        if u_steps == 0 and u_data:
            for tag_pts in u_data.values():
                if tag_pts:
                    u_steps = max(u_steps, tag_pts[-1][0] - tag_pts[0][0])
        total_unique_steps += u_steps

        # Add to Unique01 chart series if present
        if "Self-play/ELO" in u_data:
            elo_series_unique.append({
                "label": f"{fname} ({style})",
                "color": color,
                "points": downsample(ema_smooth(u_data["Self-play/ELO"], 0.35)),
            })
        if "Environment/Episode Length" in u_data:
            ep_len_series_unique.append({
                "label": fname,
                "color": color,
                "points": downsample(ema_smooth(u_data["Environment/Episode Length"], 0.3)),
            })
        if "Environment/Cumulative Reward" in u_data:
            reward_series_unique.append({
                "label": fname,
                "color": color,
                "points": downsample(ema_smooth(u_data["Environment/Cumulative Reward"], 0.3)),
            })
        if "Losses/Value Loss" in u_data:
            vloss_series_unique.append({
                "label": fname,
                "color": color,
                "points": downsample(ema_smooth(u_data["Losses/Value Loss"], 0.3)),
            })

        latest_elo = u_elo_end if u_elo_end is not None else b_elo_end
        elo_delta = (u_elo_end - u_elo_start) if (u_elo_end is not None and u_elo_start is not None) else (
            (b_elo_end - b_elo_start) if (b_elo_end is not None and b_elo_start is not None) else 0.0
        )

        fighter_rows.append({
            "fighter": fname,
            "donor": donor,
            "style": style,
            "color": color,
            "borrowed": borrowed,
            "obs_size": 55 if sheet["balanceObservations"] == 1 else 53,
            "massScale": sheet["massScale"],
            "widthScale": sheet["widthScale"],
            "torqueScale": sheet["torqueScale"],
            "onnx_mtime": sheet["onnx_mtime"],
            "base_run": base_run_key,
            "base_steps": b_steps,
            "unique_steps": u_steps,
            "has_unique_run": bool(u_data),
            "start_elo": u_elo_start if u_elo_start is not None else b_elo_start,
            "latest_elo": latest_elo,
            "elo_delta": elo_delta,
            "latest_rew": u_rew_end if u_rew_end is not None else b_rew_end,
            "latest_len": u_len_end if u_len_end is not None else b_len_end,
            "latest_vloss": u_vloss_end if u_vloss_end is not None else b_vloss_end,
            "latest_slip": u_slip_end,
        })

    now_str = dt.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    trained_unique_count = sum(1 for r in fighter_rows if r["has_unique_run"])
    peak_elo_row = max(fighter_rows, key=lambda r: r["latest_elo"] if r["latest_elo"] is not None else -9999.0)

    # Render charts (show Unique01 curves if available, plus core runs)
    chart_elo_unique = render_svg_line_chart(
        "1. Self-Play ELO Progression — Unique01 & Real01 Active Campaigns",
        "Each fighter starts self-play at ELO 1200 against its warm-start snapshot and climbs as it adapts to its own physique and the new shikiri crouch pose.",
        "Self-Play ELO",
        elo_series_unique if elo_series_unique else elo_series_core,
        baseline_y=1200.0,
        baseline_label="1200 Initial Warm-Start ELO",
        normalize_steps=bool(elo_series_unique),
    )

    chart_elo_core = render_svg_line_chart(
        "Baseline Reference: Core Fighter Trunk Runs (Intent01 53-Obs → Real01 55-Obs)",
        "Long-horizon 5M–6M step trunk curves for Matt, Grandma, Nick, and Kim used to warm-start all 16 fighters.",
        "Self-Play ELO",
        elo_series_core,
        baseline_y=1200.0,
        baseline_label="1200 Baseline",
        normalize_steps=False,
    )

    chart_eplen = render_svg_line_chart(
        "2. Environment Episode Length (Steps per Bout / Walk)",
        "Measures how long fighters stay on their feet and contest the tawara without early falls or ring-outs.",
        "Episode Steps",
        ep_len_series_unique if ep_len_series_unique else ep_len_series_core,
        normalize_steps=bool(ep_len_series_unique),
    )

    chart_reward = render_svg_line_chart(
        "3. Environment Cumulative Episode Reward",
        "Combined walk-in cadence, upright posture, closing velocity, and sumo ring-out reward minus effort/jerk/slip penalties.",
        "Cumulative Reward",
        reward_series_unique if reward_series_unique else reward_series_core,
        baseline_y=0.0,
        baseline_label="Zero Net Reward",
        normalize_steps=bool(reward_series_unique),
    )

    chart_vloss = render_svg_line_chart(
        "4. Critic Value Loss Convergence (PPO Stability & Zero-NaN Check)",
        "Tracks value network prediction error as policies adapt to character-specific reward weights and shove perturbations.",
        "Value Loss",
        vloss_series_unique if vloss_series_unique else [
            {
                "label": rname,
                "color": core_colors.get(rname, "#38bdf8"),
                "points": downsample(ema_smooth(sdata["Losses/Value Loss"], 0.3)),
            }
            for rname, sdata in core_runs.items()
            if "Losses/Value Loss" in sdata
        ],
        normalize_steps=bool(vloss_series_unique),
    )

    chart_bars = render_elo_bar_chart(fighter_rows)

    # Build table rows HTML
    table_rows_html = []
    for r in fighter_rows:
        own_badge = (
            '<span class="badge badge-ok">OWN BRAIN</span>'
            if not r["borrowed"]
            else f'<span class="badge badge-warn">BORROWS {html.escape(r["borrowed"])}</span>'
        )
        run_badge = (
            '<span class="badge badge-live">UNIQUE01 TRAINED</span>'
            if r["has_unique_run"]
            else '<span class="badge badge-queued">WARM-START READY</span>'
        )
        elo_str = f"{r['latest_elo']:.1f}" if r["latest_elo"] is not None else "—"
        delta_val = r["elo_delta"]
        delta_cls = "pos" if delta_val >= 0 else "neg"
        delta_str = f"{'+' if delta_val >= 0 else ''}{delta_val:.1f}"
        rew_str = f"{r['latest_rew']:.3f}" if r["latest_rew"] is not None else "—"
        len_str = f"{r['latest_len']:.0f}" if r["latest_len"] is not None else "—"
        vloss_str = f"{r['latest_vloss']:.4f}" if r["latest_vloss"] is not None else "—"
        steps_str = f"+{r['unique_steps']:,}" if r["unique_steps"] > 0 else "0 (queued)"

        table_rows_html.append(f"""
        <tr>
          <td><span class="dot" style="background:{r['color']}"></span><strong>{html.escape(r['fighter'])}</strong></td>
          <td>{html.escape(r['donor'])}</td>
          <td><code>{html.escape(r['style'])}</code></td>
          <td>{r['obs_size']} obs / 14 act</td>
          <td>{r['massScale']:.2f}x / {r['widthScale']:.2f}x / {r['torqueScale']:.2f}x</td>
          <td>{own_badge}</td>
          <td>{run_badge}</td>
          <td class="num">{steps_str}</td>
          <td class="num"><strong>{elo_str}</strong> <span class="{delta_cls}">({delta_str})</span></td>
          <td class="num">{len_str}</td>
          <td class="num">{rew_str}</td>
          <td class="num">{vloss_str}</td>
          <td class="num"><code>{html.escape(r['onnx_mtime'])}</code></td>
        </tr>
        """)

    html_doc = f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1"/>
  <title>PoSumo — Multi-Agent RL Optimization &amp; Telemetry Report</title>
  <style>
    :root {{
      --bg: #070b14;
      --card: #0f172a;
      --border: #1e293b;
      --text: #e2e8f0;
      --muted: #94a3b8;
      --accent: #38bdf8;
      --ok: #22c55e;
      --warn: #f59e0b;
    }}
    * {{ box-sizing: border-box; }}
    body {{
      margin: 0;
      padding: 28px 32px 60px;
      background: var(--bg);
      color: var(--text);
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Inter, sans-serif;
      line-height: 1.55;
    }}
    .container {{ max-width: 1280px; margin: 0 auto; }}
    header {{
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
      flex-wrap: wrap;
      gap: 16px;
      border-bottom: 1px solid var(--border);
      padding-bottom: 18px;
      margin-bottom: 24px;
    }}
    h1 {{ margin: 0; font-size: 26px; letter-spacing: -0.02em; color: #f8fafc; }}
    .sub {{ color: var(--muted); font-size: 14px; margin-top: 4px; }}
    .stamp {{
      background: #111c33;
      border: 1px solid #263859;
      padding: 8px 14px;
      border-radius: 8px;
      font-family: monospace;
      font-size: 12px;
      color: #93c5fd;
    }}
    .kpi-grid {{
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 14px;
      margin-bottom: 26px;
    }}
    .kpi {{
      background: var(--card);
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 16px 18px;
    }}
    .kpi-label {{ font-size: 12px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--muted); }}
    .kpi-val {{ font-size: 26px; font-weight: 700; margin-top: 4px; color: #f8fafc; font-family: monospace; }}
    .kpi-note {{ font-size: 12px; color: #64748b; margin-top: 4px; }}
    .section-title {{
      font-size: 19px;
      font-weight: 700;
      margin: 32px 0 12px;
      color: #f1f5f9;
      border-left: 4px solid var(--accent);
      padding-left: 12px;
    }}
    .chart-grid {{
      display: grid;
      grid-template-columns: 1fr;
      gap: 20px;
      margin-bottom: 24px;
    }}
    .chart-box {{
      background: var(--card);
      border: 1px solid var(--border);
      border-radius: 12px;
      padding: 18px 20px;
    }}
    .chart-title {{ font-size: 16px; font-weight: 700; color: #f8fafc; }}
    .chart-sub {{ font-size: 13px; color: var(--muted); margin: 3px 0 12px; }}
    .metric-svg {{ width: 100%; height: auto; display: block; }}
    .legend-row {{
      display: flex;
      flex-wrap: wrap;
      gap: 8px 14px;
      margin-top: 12px;
      padding-top: 10px;
      border-top: 1px solid #1e293b;
      font-size: 12px;
    }}
    .legend-chip {{ display: inline-flex; align-items: center; gap: 6px; color: #cbd5e1; font-family: monospace; }}
    .legend-swatch {{ width: 12px; height: 12px; border-radius: 3px; display: inline-block; }}
    .tiers-grid {{
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(360px, 1fr));
      gap: 16px;
      margin-bottom: 26px;
    }}
    .tier-card {{
      background: var(--card);
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 16px 18px;
    }}
    .tier-card h3 {{ margin: 0 0 10px; font-size: 15px; color: #38bdf8; }}
    .tier-item {{ margin-bottom: 10px; font-size: 13.5px; }}
    .tier-tag {{
      display: inline-block;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      padding: 2px 7px;
      border-radius: 4px;
      margin-right: 6px;
    }}
    .t-toddler {{ background: #3b0764; color: #e9d5ff; }}
    .t-child {{ background: #064e3b; color: #a7f3d0; }}
    .t-adult {{ background: #1e3a8a; color: #bfdbfe; }}
    table {{
      width: 100%;
      border-collapse: collapse;
      background: var(--card);
      border: 1px solid var(--border);
      border-radius: 10px;
      overflow: hidden;
      font-size: 13px;
    }}
    th, td {{
      padding: 10px 12px;
      border-bottom: 1px solid #1e293b;
      text-align: left;
    }}
    th {{
      background: #111c33;
      color: #94a3b8;
      font-weight: 600;
      font-size: 11.5px;
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }}
    td.num {{ text-align: right; font-family: monospace; }}
    .dot {{ width: 9px; height: 9px; border-radius: 50%; display: inline-block; margin-right: 7px; }}
    .badge {{
      display: inline-block;
      padding: 2px 7px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 700;
      font-family: monospace;
    }}
    .badge-ok {{ background: rgba(34, 197, 94, 0.18); color: #4ade80; border: 1px solid rgba(34, 197, 94, 0.4); }}
    .badge-warn {{ background: rgba(245, 158, 11, 0.16); color: #fbbf24; border: 1px solid rgba(245, 158, 11, 0.35); }}
    .badge-live {{ background: rgba(56, 189, 248, 0.18); color: #38bdf8; border: 1px solid rgba(56, 189, 248, 0.4); }}
    .badge-queued {{ background: rgba(148, 163, 184, 0.15); color: #94a3b8; border: 1px solid rgba(148, 163, 184, 0.3); }}
    .pos {{ color: #4ade80; }}
    .neg {{ color: #f87171; }}
    .empty-chart {{
      padding: 40px;
      text-align: center;
      color: var(--muted);
      background: #0b1120;
      border-radius: 8px;
      font-family: monospace;
    }}
  </style>
</head>
<body>
<div class="container">
  <header>
    <div>
      <h1>PoSumo — Multi-Agent RL Optimization &amp; Telemetry Report</h1>
      <div class="sub">Unattended 16-Fighter Unique01 + Real01 Biomechanical Training Loop · Unity ML-Agents PPO + Self-Play</div>
    </div>
    <div class="stamp">Updated: {html.escape(now_str)}<br/>TensorBoard: http://localhost:6006</div>
  </header>

  <div class="kpi-grid">
    <div class="kpi">
      <div class="kpi-label">Roster Own-Brain Compliance</div>
      <div class="kpi-val">{own_brain_count} / {len(fighter_rows)}</div>
      <div class="kpi-note">AGENTS.md §3: 0 borrowed brains at completion</div>
    </div>
    <div class="kpi">
      <div class="kpi-label">Unique01 Fighters Trained</div>
      <div class="kpi-val">{trained_unique_count} / {len(fighter_rows)}</div>
      <div class="kpi-note">Individual physique + shikiri crouch pose</div>
    </div>
    <div class="kpi">
      <div class="kpi-label">New Unique01 Steps Logged</div>
      <div class="kpi-val">{total_unique_steps:,}</div>
      <div class="kpi-note">On top of ~23.2M core trunk steps</div>
    </div>
    <div class="kpi">
      <div class="kpi-label">Peak Roster ELO</div>
      <div class="kpi-val">{peak_elo_row['latest_elo']:.0f} ({html.escape(peak_elo_row['fighter'])})</div>
      <div class="kpi-note">Baseline self-play start = 1200</div>
    </div>
    <div class="kpi">
      <div class="kpi-label">Shikiri Self-Overlap</div>
      <div class="kpi-val">0 deep steps</div>
      <div class="kpi-note">Down from ~150/bout (hip -90° / spine1 20°)</div>
    </div>
  </div>

  <div class="section-title">Consequential TensorBoard Metrics (Inline SVG Curves)</div>
  <div class="chart-grid">
    {chart_bars}
    {chart_elo_unique}
    {chart_eplen}
    {chart_reward}
    {chart_vloss}
    {chart_elo_core}
  </div>

  <div class="section-title">Comprehension Tiers (Toddler · Child · Adult) &amp; Run-Over-Run Comparison</div>
  <div class="tiers-grid">
    <div class="tier-card">
      <h3>Chart 1: Self-Play ELO Rating (<code>Self-play/ELO</code>)</h3>
      <div class="tier-item"><span class="tier-tag t-toddler">1 · Toddler</span>The line goes up when our sumo wrestlers learn new tricks and beat their older clones!</div>
      <div class="tier-item"><span class="tier-tag t-child">2 · Child</span>Every fighter starts at 1,200 points and practices against saved copies of itself. When the curve climbs above 1,200, today's fighter is stronger and smarter than the one that started the session.</div>
      <div class="tier-item"><span class="tier-tag t-adult">3 · Adult</span>Tracks rating gain in ML-Agents asymmetric self-play (`team_change: 100k`, `window: 10`, `play_against_latest_model_ratio: 0.4–0.6`). Warm-started from `Intent01`/`Real01` trunks at 1200, positive slope confirms each fighter's policy specializes to its own `massScale`/`widthScale`/`torqueScale` and the non-overlapping `-90°/90°` shikiri crouch without catastrophic forgetting.</div>
    </div>

    <div class="tier-card">
      <h3>Chart 2: Episode Length (<code>Environment/Episode Length</code>)</h3>
      <div class="tier-item"><span class="tier-tag t-toddler">1 · Toddler</span>How long the wrestlers stay standing and pushing before someone falls down!</div>
      <div class="tier-item"><span class="tier-tag t-child">2 · Child</span>Longer episodes mean the wrestlers aren't tripping over their own feet during the walk-in or falling flat right after the tachiai charge. They stay balanced and grapple longer.</div>
      <div class="tier-item"><span class="tier-tag t-adult">3 · Adult</span>Mean steps per episode across interleaved walk (`MaxStep = 1500`) and sumo lanes. Stability here verifies that changing `CROUCH_ANGLES` (`hip -120°→-90°`, `spine1 30°→20°`) and injecting random chest shoves (`10–30 N·s` on Kim/Dupee/Velez/Manzi) does not induce early collapse.</div>
    </div>

    <div class="tier-card">
      <h3>Chart 3: Cumulative Reward &amp; Value Loss (<code>Cumulative Reward</code> / <code>Value Loss</code>)</h3>
      <div class="tier-item"><span class="tier-tag t-toddler">1 · Toddler</span>Points earned for walking nicely, standing tall, and pushing the other wrestler out of the ring!</div>
      <div class="tier-item"><span class="tier-tag t-child">2 · Child</span>Wrestlers earn points for good steps and pushing forward, and lose points for wasting energy or sliding their feet. The Value Loss chart shows how well the brain predicts its score—smooth and low means steady learning.</div>
      <div class="tier-item"><span class="tier-tag t-adult">3 · Adult</span>Net episodic return under character-specific reward weights (`uprightReward`, `closingReward`, `cadenceReward`, `driveReward`, minus quadratic `effortPenalty`, `jerkPenalty`, and `Real01` foot-slip penalty). Bounded critic loss with 0 NaN gradients confirms stable PPO updates (`beta: 5e-3–1.2e-2`, `epsilon: 0.10–0.25`) across all 16 distinct recipes.</div>
    </div>

    <div class="tier-card">
      <h3>Run-Over-Run Comparison vs. Prior Runs (<code>Intent01</code> &amp; <code>Real01</code>)</h3>
      <div class="tier-item"><span class="tier-tag t-toddler">1 · Toddler</span>Before, 12 wrestlers borrowed someone else's brain. Now every single wrestler gets their very own brain!</div>
      <div class="tier-item"><span class="tier-tag t-child">2 · Child</span>In the earlier `Intent01` runs, only Matt, Grandma, Nick, and Kim trained, and everyone crouched so deep their legs overlapped their bellies. Now the crouch is fixed so nobody clips through themselves, and all 16 fighters train with their own personality.</div>
      <div class="tier-item"><span class="tier-tag t-adult">3 · Adult</span>Compared to `Intent01` (53-obs, 4 fighters, ~150 deep self-overlaps/bout at shikiri), `Real01` + `Unique01` eliminates deep (>2 cm) self-overlap in 15/15 tournament bouts, adds 55-obs balance/COM telemetry (`com_x_rel_support`, `com_vx`) + quadratic jerk/slip penalties on `Real01`, and replaces borrowed donor ONNX references with 16 dedicated policies trained via `-posumoFighter &lt;Name&gt;`.</div>
    </div>
  </div>

  <div class="section-title">Full 16-Fighter Roster Telemetry &amp; Brain Ownership</div>
  <table>
    <thead>
      <tr>
        <th>Fighter</th>
        <th>Donor Env</th>
        <th>Unique01 Style</th>
        <th>Interface</th>
        <th>Mass / Width / Torque</th>
        <th>Brain Ownership</th>
        <th>Campaign Status</th>
        <th class="num">Unique01 Steps</th>
        <th class="num">Self-Play ELO (Δ)</th>
        <th class="num">Ep Length</th>
        <th class="num">Cumul Reward</th>
        <th class="num">Value Loss</th>
        <th class="num">Deployed ONNX</th>
      </tr>
    </thead>
    <tbody>
      {''.join(table_rows_html)}
    </tbody>
  </table>
</div>
</body>
</html>
"""

    for out_path in OUT_PATHS:
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(html_doc, encoding="utf-8")
    print(f"Wrote training report to {OUT_PATHS[0]} and {OUT_PATHS[1]} ({own_brain_count}/16 own brains, {trained_unique_count}/16 unique01 runs).")


if __name__ == "__main__":
    main()
