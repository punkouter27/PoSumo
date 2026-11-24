"""Before/after screenshot report for a UI change, in one command.

AGENTS.md asks that UI changes get before/after screenshots and an HTML report in SCREENSHOTS/.
This captures PoSumo's key screens from a LOCAL WebGL build (never the live site) at 1366x768 and
390x844 and writes, with the folder naming the earlier reports use:

  SCREENSHOTS/<YYYYMMDD>_<feature>/before/<screen>_<w>x<h>.png
  SCREENSHOTS/<YYYYMMDD>_<feature>/after/<screen>_<w>x<h>.png
  SCREENSHOTS/<YYYYMMDD>_<feature>/report.html     before | after per screen, with notes
  SCREENSHOTS/<YYYYMMDD>_<feature>/<feature>.png   every pair side by side, changed area boxed in red

"Before" comes from one of:
  --before <git ref>      (default HEAD~1) build that commit's WebGL player in a temporary detached
                          worktree (scripts/publish_webgl.ps1 -NoPublish -SkipTests there) and capture it;
                          a fresh worktree imports the whole project, so this is slow (tens of minutes)
  --before-build <folder> an existing WebGL build folder of the old version
  --before-dir <folder>   PNGs whose names hold the screen name and the width (e.g. home-1366-before.png)

"After" is Builds/WebGL (build it first: pwsh -File scripts/publish_webgl.ps1 -NoPublish).

  python scripts/screenshots.py --feature diag-cache --before-dir <pngs> --notes "what changed"

Needs: pip install playwright pillow && python -m playwright install chromium
Images are downscaled and palette-quantised (the repo is git, not an image store).
"""

from __future__ import annotations

import argparse
import contextlib
import datetime as dt
import functools
import html
import http.server
import shutil
import subprocess
import sys
import tempfile
import threading
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SIZES = ((1366, 768), (390, 844))
DEFAULT_SCREENS = "game=/,diag=/diag"
MAX_WIDTH = 683  # half of 1366: keeps each PNG well under 300 KB
GAME_WAIT_S = 45.0


def folder_name(feature: str, day: dt.date) -> str:
    """SCREENSHOTS/<YYYYMMDD>_<feature>, the naming of the earlier reports."""
    safe = "".join(c if c.isalnum() or c in "-_" else "-" for c in feature.strip()).strip("-")
    if not safe:
        raise ValueError("feature name is empty")
    return f"{day:%Y%m%d}_{safe}"


def parse_screens(spec: str) -> dict[str, str]:
    screens = {}
    for item in spec.split(","):
        name, _, route = item.partition("=")
        if not name.strip() or not route.startswith("/"):
            raise ValueError(f"screen {item!r} must be name=/route")
        screens[name.strip()] = route
    return screens


def shot_name(screen: str, size: tuple[int, int]) -> str:
    return f"{screen}_{size[0]}x{size[1]}.png"


class _Handler(http.server.SimpleHTTPRequestHandler):
    """Serves a WebGL build like scripts/webgl/staticwebapp.config.json does: /diag -> diag.html,
    .unityweb is gzip (Content-Encoding), X-Commit present so /diag's commit row has a value."""

    def translate_path(self, path):
        if path.split("?", 1)[0].rstrip("/") == "/diag":
            path = "/diag.html"
        return super().translate_path(path)

    def end_headers(self):
        if self.path.split("?", 1)[0].endswith(".unityweb"):
            self.send_header("Content-Encoding", "gzip")
        self.send_header("X-Commit", "local-build")
        super().end_headers()

    def guess_type(self, path):
        name = str(path)
        if name.endswith(".wasm.unityweb") or name.endswith(".wasm"):
            return "application/wasm"
        if name.endswith(".js.unityweb"):
            return "application/javascript"
        if name.endswith(".unityweb"):
            return "application/octet-stream"
        return super().guess_type(path)

    def log_message(self, *args):
        pass


@contextlib.contextmanager
def serve(build: Path):
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), functools.partial(_Handler, directory=str(build)))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    try:
        yield f"http://127.0.0.1:{server.server_address[1]}"
    finally:
        server.shutdown()
        server.server_close()


def capture(build: Path, screens: dict[str, str], out: Path, wait_s: float) -> dict[tuple[str, int], Path]:
    """One screenshot per screen and size; the game page waits until Unity has loaded, then wait_s."""
    from playwright.sync_api import sync_playwright

    out.mkdir(parents=True, exist_ok=True)
    shots = {}
    with serve(build) as base, sync_playwright() as pw:
        browser = pw.chromium.launch(args=["--ignore-gpu-blocklist", "--enable-unsafe-swiftshader"])
        try:
            for name, route in screens.items():
                for size in SIZES:
                    page = browser.new_page(viewport={"width": size[0], "height": size[1]})
                    page.goto(base + route, wait_until="load", timeout=120_000)
                    if route in ("/", "/index.html"):
                        # The loading overlay hides when createUnityInstance resolves.
                        with contextlib.suppress(Exception):
                            page.wait_for_selector("#loading", state="hidden", timeout=180_000)
                        page.wait_for_timeout(int(wait_s * 1000))
                    else:
                        page.wait_for_timeout(3000)
                    path = out / shot_name(name, size)
                    page.screenshot(path=str(path))
                    page.close()
                    shots[(name, size[0])] = path
        finally:
            browser.close()
    return shots


def find_before(before_dir: Path, screens: dict[str, str]) -> dict[tuple[str, int], Path]:
    """Matches PNGs by screen name and width in the file name ('after' files are skipped)."""
    pngs = sorted(p for p in before_dir.glob("*.png") if "after" not in p.stem.lower())
    found = {}
    for name in screens:
        for w, _ in SIZES:
            hits = [p for p in pngs if name in p.stem.lower() and str(w) in p.stem]
            if hits:
                found[(name, w)] = hits[0]
    return found


def build_ref(ref: str) -> Path:
    """Builds `ref` in a temporary detached worktree and returns a copy of its Builds/WebGL."""
    tmp = Path(tempfile.mkdtemp(prefix="posumo-before-"))
    tree = tmp / "tree"
    subprocess.run(["git", "-C", str(ROOT), "worktree", "add", "--detach", str(tree), ref], check=True)
    try:
        subprocess.run(
            ["pwsh", "-File", str(tree / "scripts" / "publish_webgl.ps1"), "-NoPublish", "-SkipTests"], check=True
        )
        out = tmp / "WebGL"
        shutil.copytree(tree / "Builds" / "WebGL", out)
        return out
    finally:
        subprocess.run(["git", "-C", str(ROOT), "worktree", "remove", "--force", str(tree)], check=False)


def shrink(src: Path, dst: Path) -> Path:
    from PIL import Image

    dst.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(src) as im:
        im = im.convert("RGB")
        if im.width > MAX_WIDTH:
            im = im.resize((MAX_WIDTH, round(im.height * MAX_WIDTH / im.width)), Image.LANCZOS)
        im.quantize(colors=128, method=Image.Quantize.MEDIANCUT).save(dst, optimize=True)
    return dst


def changed_box(before: Path, after: Path) -> tuple[int, int, int, int] | None:
    """Bounding box of the pixels that differ (same-size images), or None when nothing did."""
    from PIL import Image, ImageChops

    with Image.open(before) as b, Image.open(after) as a:
        if b.size != a.size:
            return (0, 0, a.width, a.height)
        diff = (
            ImageChops.difference(b.convert("RGB"), a.convert("RGB")).convert("L").point(lambda v: 255 if v > 24 else 0)
        )
        return diff.getbbox()


def side_by_side(pairs, dst: Path, title: str) -> None:
    """One annotated sheet: BEFORE | AFTER per screen; the changed area is boxed in red on the after."""
    from PIL import Image, ImageDraw

    pad, label = 12, 26
    loaded = []
    for caption, before, after, box in pairs:
        loaded.append(
            (caption, Image.open(before).convert("RGB") if before else None, Image.open(after).convert("RGB"), box)
        )
    col = max(a.width for _, _, a, _ in loaded)
    height = label + sum(label + max(a.height, b.height if b else 0) + pad for _, b, a, _ in loaded)
    sheet = Image.new("RGB", (col * 2 + pad * 3, height), "#0b0f17")
    draw = ImageDraw.Draw(sheet)
    draw.text((pad, 7), title, fill="#e6e9f2")
    y = label
    for caption, b, a, box in loaded:
        note = "no before image" if b is None else "no visible change" if box is None else "changed area boxed in red"
        draw.text((pad, y + 7), f"BEFORE  {caption}", fill="#9aa3b5")
        draw.text((col + pad * 2, y + 7), f"AFTER  {caption}  ({note})", fill="#e6e9f2")
        y += label
        if b:
            sheet.paste(b, (pad, y))
        else:
            draw.text((pad, y + 7), "(no before image)", fill="#f87171")
        x0 = col + pad * 2
        sheet.paste(a, (x0, y))
        if box:
            draw.rectangle((x0 + box[0], y + box[1], x0 + box[2] - 1, y + box[3] - 1), outline="#ef4444", width=3)
        y += max(a.height, b.height if b else 0) + pad
    sheet.quantize(colors=128, method=Image.Quantize.MEDIANCUT).save(dst, optimize=True)


def write_report(folder: Path, feature: str, pairs, notes: str, before_label: str, image: str) -> Path:
    cards = []
    for caption, before, after, box in pairs:
        rel_b = before.relative_to(folder).as_posix() if before else ""
        rel_a = after.relative_to(folder).as_posix()
        change = (
            "No before image."
            if before is None
            else "No visible change."
            if box is None
            else f"Changed area (red box on the sheet): x {box[0]}-{box[2]}, y {box[1]}-{box[3]}."
        )
        img_b = (
            f'<img src="{html.escape(rel_b)}" alt="Before: {html.escape(caption)}">'
            if before
            else "<p>(no before image)</p>"
        )
        cards.append(
            f'<section><h2>{html.escape(caption)}</h2><p class="sub">{html.escape(change)}</p><div class="pair">'
            f"<figure><figcaption>Before</figcaption>{img_b}</figure>"
            f'<figure><figcaption>After</figcaption><img src="{html.escape(rel_a)}" alt="After: {html.escape(caption)}"></figure>'
            "</div></section>"
        )
    page = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>PoSumo {html.escape(feature)}</title>
<style>
:root{{--bg:#0b0f17;--fg:#e6e9f2;--muted:#9aa3b5;--card:#121826;--line:#232c3f}}
body{{margin:0;background:var(--bg);color:var(--fg);font:15px/1.5 Inter,system-ui,sans-serif}}
main{{max-width:1400px;margin:0 auto;padding:24px 16px 48px}}h1{{margin:0 0 4px;font-size:26px}}
.sub{{color:var(--muted);margin:0 0 12px}}a{{color:#a5b4fc}}section{{margin-top:28px}}
.pair{{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:16px;align-items:start}}
figure{{margin:0;background:var(--card);border:1px solid var(--line);border-radius:10px;padding:10px}}
figcaption{{font-weight:600;margin-bottom:8px}}img{{max-width:100%;height:auto;display:block;border-radius:6px}}
</style></head>
<body><main>
<h1>PoSumo: {html.escape(feature)}</h1>
<p class="sub">Before: {html.escape(before_label)}. After: the local WebGL build (Builds/WebGL), captured
{dt.datetime.now():%Y-%m-%d %H:%M} at 1366x768 and 390x844 by scripts/screenshots.py. Every pair in one annotated
image: <a href="{html.escape(image)}">{html.escape(image)}</a>.</p>
<p>{html.escape(notes)}</p>
{"".join(cards)}
</main></body></html>
"""
    path = folder / "report.html"
    path.write_text(page, encoding="utf-8")
    return path


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--feature", required=True, help="short name for the folder and the sheet image")
    src = ap.add_mutually_exclusive_group()
    src.add_argument("--before", default="HEAD~1", help="git ref to build and capture as 'before' (default HEAD~1)")
    src.add_argument("--before-build", type=Path, help="existing WebGL build folder of the old version")
    src.add_argument("--before-dir", type=Path, help="folder of 'before' PNGs (name holds screen and width)")
    ap.add_argument("--build", type=Path, default=ROOT / "Builds" / "WebGL", help="the 'after' WebGL build")
    ap.add_argument("--screens", default=DEFAULT_SCREENS, help="name=/route pairs, comma separated")
    ap.add_argument("--wait", type=float, default=GAME_WAIT_S, help="seconds the game gets after loading")
    ap.add_argument("--notes", default="", help="what changed, shown on the report")
    args = ap.parse_args(argv)

    if not (args.build / "index.html").exists():
        sys.exit(f"No WebGL build at {args.build}. Run: pwsh -File scripts/publish_webgl.ps1 -NoPublish")
    screens = parse_screens(args.screens)
    folder = ROOT / "SCREENSHOTS" / folder_name(args.feature, dt.date.today())
    work = Path(tempfile.mkdtemp(prefix="posumo-shots-"))
    try:
        after_raw = capture(args.build, screens, work / "after", args.wait)
        if args.before_dir:
            before_raw, before_label = find_before(args.before_dir, screens), f"PNGs from {args.before_dir.name}/"
        elif args.before_build:
            before_raw = capture(args.before_build, screens, work / "before", args.wait)
            before_label = f"local WebGL build {args.before_build}"
        else:
            old = build_ref(args.before)
            before_raw, before_label = (
                capture(old, screens, work / "before", args.wait),
                f"local WebGL build of {args.before}",
            )

        pairs = []
        for name in screens:
            for size in SIZES:
                after = shrink(after_raw[(name, size[0])], folder / "after" / shot_name(name, size))
                raw = before_raw.get((name, size[0]))
                before = shrink(raw, folder / "before" / shot_name(name, size)) if raw else None
                box = changed_box(before, after) if before else None
                pairs.append((f"{name} {size[0]}x{size[1]}", before, after, box))
    finally:
        shutil.rmtree(work, ignore_errors=True)

    image = f"{folder.name.split('_', 1)[1]}.png"
    side_by_side(pairs, folder / image, f"PoSumo {args.feature}: before | after")
    report = write_report(folder, args.feature, pairs, args.notes, before_label, image)
    print(f"Wrote {report.relative_to(ROOT).as_posix()} and {image}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
