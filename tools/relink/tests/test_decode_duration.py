"""decode_duration reads the decoded length from ffmpeg's progress output, whichever keys the build prints.

A 2018 git build prints no ``out_time_us``; its progress block has ``out_time_ms`` (holding microseconds) and
``out_time``. The wrapper below stands in for such builds: it runs the real ffmpeg and rewrites the progress lines.
"""

from __future__ import annotations

import logging
import os
import shutil
import stat
import subprocess
from collections.abc import Callable
from pathlib import Path

import pytest

from test_oplinks import PRINCESS, PRINCESS_ASIN, add, container_says, edition_folder, lib, links_file, move_to, plan
from test_size_placeholder import Library, library

from listenarr_relink import media

__all__ = ["lib", "library"]

pytestmark = pytest.mark.skipif(shutil.which("ffmpeg") is None, reason="needs ffmpeg")

SECONDS = 3

# An ffmpeg stand-in: the real ffmpeg runs, and the progress lines it prints on stdout are rewritten per MODE.
WRAPPER = """#!/usr/bin/env python3
import os, subprocess, sys
mode = "MODE"
result = subprocess.run(["REAL", *sys.argv[1:]], capture_output=True, text=True)
out = []
for line in result.stdout.splitlines():
    key = line.partition("=")[0]
    if mode == "old-build" and key == "out_time_us":
        continue  # the 2018 build: out_time_ms (microseconds) and out_time only
    if mode == "ms-only" and key in ("out_time_us", "out_time"):
        continue
    if mode == "not-available" and key in ("out_time_us", "out_time_ms", "out_time"):
        line = key + "=N/A"
    if mode == "garbage" and key in ("out_time_us", "out_time_ms", "out_time"):
        line = key + "=soon"
    if mode == "no-progress" and key in ("out_time_us", "out_time_ms", "out_time"):
        continue
    out.append(line)
sys.stdout.write(chr(10).join(out) + chr(10))
sys.stderr.write(result.stderr)
sys.exit(result.returncode)
"""


def install_shim(folder: Path, mode: str) -> Path:
    """Write an ``ffmpeg`` into ``folder`` that rewrites the progress output with ``mode``, then runs the real one."""
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
def build(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Callable[[str], None]:
    """Put an ``ffmpeg`` printing the progress of the given kind of build first on PATH."""

    def put(mode: str) -> None:
        folder = tmp_path / f"build-{mode}"
        install_shim(folder, mode)
        monkeypatch.setenv("PATH", f"{folder}{os.pathsep}{os.environ['PATH']}")

    return put


def test_decodes_the_stored_length(audio: str) -> None:
    decoded = media.decode_duration(audio)
    assert decoded is not None and abs(decoded - SECONDS) < 0.1


def test_old_build_shim_has_no_out_time_us(audio: str, build: Callable[[str], None]) -> None:
    """Control: the shim prints the 2018 build's progress (microsecond out_time_ms and out_time, no out_time_us)."""
    command = ["ffmpeg", "-nostdin", "-v", "error", "-progress", "pipe:1", "-nostats", "-i", audio, "-map", "0:a:0",
               "-f", "null", "-"]
    modern = subprocess.run(command, capture_output=True, text=True, check=True).stdout
    build("old-build")
    old = subprocess.run(command, capture_output=True, text=True, check=True).stdout
    assert "out_time_us=" in modern
    assert "out_time_us=" not in old and f"out_time_ms={SECONDS * 1_000_000}" in old and "out_time=00:00:03" in old


@pytest.mark.parametrize("mode", ["old-build", "ms-only"])
def test_decodes_on_a_build_without_out_time_us(audio: str, build: Callable[[str], None], mode: str) -> None:
    build(mode)
    decoded = media.decode_duration(audio)
    assert decoded is not None and abs(decoded - SECONDS) < 0.1


@pytest.mark.parametrize("mode", ["not-available", "garbage", "no-progress"])
def test_a_build_with_no_usable_progress_gives_none(audio: str, build: Callable[[str], None], mode: str) -> None:
    build(mode)
    assert media.decode_duration(audio) is None


@pytest.mark.parametrize(("progress", "expected"), [
    ("out_time_us=5000000\nout_time_ms=5000000\nout_time=00:00:05.000000", (5.0, "out_time_us")),
    ("out_time_ms=5000000\nout_time=00:00:05.000000\nprogress=end", (5.0, "out_time")),
    ("out_time_ms=5000000\nprogress=end", (5.0, "out_time_ms")),
    ("out_time_us=N/A\nout_time_ms=N/A\nout_time=N/A", None),
    ("out_time_us=N/A\nout_time=01:02:03.500000", (3723.5, "out_time")),
    ("out_time_us=1000000\nout_time_us=N/A", (1.0, "out_time_us")),  # the last number counts, N/A is ignored
    ("out_time_us=soon\nout_time_ms=-5\nout_time=-00:00:01.000000", None),
    ("", None),
])
def test_progress_seconds_reads_the_best_available_key(progress: str, expected: tuple[float, str] | None) -> None:
    assert media.progress_seconds(progress) == expected


def test_a_decode_without_progress_is_logged_as_a_count_with_no_path(
        lib: Library, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, caplog: pytest.LogCaptureFixture) -> None:
    """A build that prints no progress time gives one summary line with the number only, never a path."""
    folder = tmp_path / "no-progress"
    install_shim(folder, "no-progress")
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
