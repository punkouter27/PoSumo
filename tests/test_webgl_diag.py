"""scripts/webgl_diag.py writes the /diag page: live probes only, one stamp, every bundle file."""

import json

import pytest
import webgl_diag


def test_repo_config_is_valid():
    game, checks = webgl_diag.load_checks()
    assert game == "PoSumo"
    assert all(check["section"] in webgl_diag.SECTIONS for check in checks)


def test_bad_section_is_rejected(tmp_path):
    cfg = tmp_path / "webgl.json"
    cfg.write_text(json.dumps({"diagChecks": [{"section": "Misc", "name": "x", "url": "y"}]}))
    with pytest.raises(ValueError):
        webgl_diag.load_checks(cfg)


def test_page_probes_every_bundle_file_and_carries_the_stamp(tmp_path):
    build = tmp_path / "Build"
    build.mkdir()
    for name in ("WebGL.loader.js", "WebGL.data.unityweb", "WebGL.wasm.unityweb"):
        (build / name).write_bytes(b"x")
    out = webgl_diag.write(tmp_path, "261120")
    page = out.read_text(encoding="utf-8")
    assert "<h1>System status</h1>" in page
    assert "v261120" in page
    for name in ("WebGL.loader.js", "WebGL.data.unityweb", "WebGL.wasm.unityweb"):
        assert f"Build/{name}" in page
    # Nothing is declared healthy up front: every status starts as "checking" and comes from fetch().
    assert "Ready" not in page
    assert "fetch(c.url" in page
    assert "getContext('webgl2')" in page


def test_missing_build_folder_fails(tmp_path):
    with pytest.raises(FileNotFoundError):
        webgl_diag.write(tmp_path, "261120")


def test_stamp_must_be_yymmdd(tmp_path):
    with pytest.raises(SystemExit):
        webgl_diag.main([str(tmp_path), "1.0.4"])


def test_repo_config_probes_the_deployed_commit_header():
    _, checks = webgl_diag.load_checks()
    commit = [check for check in checks if check.get("header", "").lower() == "x-commit"]
    assert commit, "/diag must show the live X-Commit (the deployed commit)"
    page = webgl_diag.render("PoSumo", "261122", checks)
    assert "r.headers.get(c.header)" in page
