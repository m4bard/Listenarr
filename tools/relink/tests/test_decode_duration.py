"""decode_duration reads the decoded length from ffmpeg's progress output, on a build that ignores trailing options.

An old ffmpeg build (2018) treats every option after the output target as a trailing option and ignores it, so a
progress request placed after the output prints nothing. The wrapper below stands in for such a build: it drops
every argument after the first ``-`` (the output target) and then runs the real ffmpeg.
"""

from __future__ import annotations

import os
import shutil
import stat
import subprocess
from pathlib import Path

import pytest

from listenarr_relink import media

pytestmark = pytest.mark.skipif(shutil.which("ffmpeg") is None, reason="needs ffmpeg")

SECONDS = 3

WRAPPER = """#!/bin/sh
real="$1"
shift
keep=""
for arg in "$@"; do
    keep="$keep $arg"
    if [ "$arg" = "-" ]; then
        break
    fi
done
exec "$real" $keep
"""


@pytest.fixture
def audio(tmp_path: Path) -> str:
    path = tmp_path / "tone.mp3"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", f"sine=frequency=440:duration={SECONDS}",
                    str(path)], check=True)
    return str(path)


@pytest.fixture
def old_build(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    """Put an ``ffmpeg`` first on PATH that ignores every option after the output target."""
    real = shutil.which("ffmpeg")
    assert real is not None
    folder = tmp_path / "old-build"
    folder.mkdir()
    shim = folder / "ffmpeg"
    shim.write_text(WRAPPER.replace('real="$1"\nshift\n', f'real="{real}"\n'))
    shim.chmod(shim.stat().st_mode | stat.S_IXUSR)
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
