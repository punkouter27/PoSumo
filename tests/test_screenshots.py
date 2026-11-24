"""scripts/screenshots.py: report naming, screen list and before-PNG matching (no browser needed)."""

import datetime as dt

import pytest

import screenshots


def test_folder_uses_the_reports_date_and_feature_naming():
    assert screenshots.folder_name("v4 audit", dt.date(2026, 11, 24)) == "20261124_v4-audit"
    with pytest.raises(ValueError):
        screenshots.folder_name("  ", dt.date(2026, 11, 24))


def test_both_required_sizes_are_captured():
    assert (1366, 768) in screenshots.SIZES and (390, 844) in screenshots.SIZES
    assert screenshots.shot_name("diag", (390, 844)) == "diag_390x844.png"


def test_screens_parse_and_reject_bad_routes():
    assert screenshots.parse_screens("game=/,diag=/diag") == {"game": "/", "diag": "/diag"}
    with pytest.raises(ValueError):
        screenshots.parse_screens("game=index.html")


def test_before_pngs_match_by_screen_and_width(tmp_path):
    for name in ("before-game-1366.png", "game-390-before.png", "diag-1366-after.png"):
        (tmp_path / name).write_bytes(b"")
    found = screenshots.find_before(tmp_path, {"game": "/", "diag": "/diag"})
    assert found[("game", 1366)].name == "before-game-1366.png"
    assert found[("game", 390)].name == "game-390-before.png"
    assert ("diag", 1366) not in found, "an 'after' PNG is never used as 'before'"
