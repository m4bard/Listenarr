"""decode_duration reads the decoded length from ffmpeg's progress output, on a build that ignores trailing options.

An old ffmpeg build (2018) treats every option after the output target as a trailing option and ignores it, so a
progress request placed after the output prints nothing. The wrapper below stands in for such a build: it drops
every argument after the first ``-`` (the output target) and then runs the real ffmpeg.
"""

from __future__ import annotations

import logging
import os
import shutil
import stat
import subprocess
from pathlib import Path

import pytest

from test_oplinks import PRINCESS, PRINCESS_ASIN, add, container_says, edition_folder, lib, links_file, move_to, plan
from test_size_placeholder import Library, library

from listenarr_relink import media

__all__ = ["lib", "library"]

pytestmark = pytest.mark.skipif(shutil.which("ffmpeg") is None, reason="needs ffmpeg")

SECONDS = 3

WRAPPER = """#!/usr/bin/env python3
import os
import sys
args = sys.argv[1:]
MODE
os.execv("REAL", ["REAL", *args])
"""
DROP_AFTER_OUTPUT = "args = args[: args.index('-') + 1]"
DROP_PROGRESS = (
    "i = args.index('-progress')\n"
    "del args[i : i + 2]"
)


def install_shim(folder: Path, mode: str) -> Path:
    """Write an ``ffmpeg`` into ``folder`` that rewrites its arguments with ``mode``, then runs the real one."""
    real = shutil.which("ffmpeg")
    assert real is not None
    folder.mkdir()
    shim = folder / "ffmpeg"
    shim.write_text(WRAPPER.replace("MODE", mode).replace("REAL", real))
    shim.chmod(shim.stat().st_mode | stat.S_IXUSR)
    return shim


@pytest.fixture
def audio(tmp_path: Path) -> str:
    path = tmp_path / "tone.mp3"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", f"sine=frequency=440:duration={SECONDS}",
                    str(path)], check=True)
    return str(path)


@pytest.fixture
def old_build(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    """Put an ``ffmpeg`` first on PATH that ignores every option after the output target."""
    folder = tmp_path / "old-build"
    shim = install_shim(folder, DROP_AFTER_OUTPUT)
    monkeypatch.setenv("PATH", f"{folder}{os.pathsep}{os.environ['PATH']}")
    return shim


def test_decodes_the_stored_length(audio: str) -> None:
    decoded = media.decode_duration(audio)
    assert decoded is not None and abs(decoded - SECONDS) < 0.1


def test_old_build_wrapper_drops_trailing_progress(audio: str, old_build: Path) -> None:
    """Control: with options after the output the wrapper prints no progress, so the apparatus can fail."""
    trailing = subprocess.run(["ffmpeg", "-nostdin", "-v", "error", "-i", audio, "-map", "0:a:0", "-f", "null", "-",
                               "-progress", "pipe:1", "-nostats"], capture_output=True, text=True, check=True)
    leading = subprocess.run(["ffmpeg", "-nostdin", "-v", "error", "-progress", "pipe:1", "-nostats", "-i", audio,
                              "-map", "0:a:0", "-f", "null", "-"], capture_output=True, text=True, check=True)
    assert "out_time_us=" not in trailing.stdout
    assert "out_time_us=" in leading.stdout


def test_decodes_on_a_build_that_ignores_trailing_options(audio: str, old_build: Path) -> None:
    decoded = media.decode_duration(audio)
    assert decoded is not None and abs(decoded - SECONDS) < 0.1


def test_a_decode_without_progress_is_logged_as_a_count_with_no_path(
        lib: Library, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, caplog: pytest.LogCaptureFixture) -> None:
    """A build that prints no out_time_us gives one summary line with the number only, never a path."""
    folder = tmp_path / "no-progress"
    install_shim(folder, DROP_PROGRESS)
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    target = move_to(lib, row, edition_folder(lib, PRINCESS, PRINCESS_ASIN))
    container_says(monkeypatch, 100.0)  # the duration veto fires, so the link needs the decode
    before = media.no_progress_decode_count()
    monkeypatch.setenv("PATH", f"{folder}{os.pathsep}{os.environ['PATH']}")
    with caplog.at_level(logging.WARNING):
        rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "duration-decode")])))
    assert rc == 0 and after is not None
    counts = after["operator_links"]["counts"]
    assert "operator_links_refused_books decode-unavailable 1" in counts  # not folded into a real mismatch
    assert not any("decode-differs-from-stored" in line for line in counts)
    assert media.no_progress_decode_count() == before + 1
    lines = [r.getMessage() for r in caplog.records if "out_time_us" in r.getMessage()]
    assert lines == ["decode exited 0 without out_time_us for 1 files; ffmpeg may ignore -progress"]
    assert str(target) not in caplog.text and str(lib.top) not in caplog.text
