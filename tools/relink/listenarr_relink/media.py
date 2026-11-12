"""Read-only audio evidence via ffprobe (stream facts, tags) and ffmpeg (audio-stream hash)."""

from __future__ import annotations

import json
import logging
import re
import subprocess
from dataclasses import dataclass

log = logging.getLogger("relink.media")

PROBE_TIMEOUT_SECONDS = 120
HASH_TIMEOUT_SECONDS = 1800


@dataclass(frozen=True)
class Probe:
    """What ffprobe says about one file; ``error`` set when it could not be read as audio."""

    duration: float | None
    codec: str | None
    sample_rate: int | None
    channels: int | None
    bit_rate: int | None
    tags: dict[str, str]
    error: str | None = None

    @property
    def track(self) -> int | None:
        """Track number tag (``3`` or ``3/12``)."""
        raw = self.tags.get("track", "").split("/")[0].strip()
        return int(raw) if raw.isdigit() else None


def _int(value: object) -> int | None:
    try:
        return int(str(value))
    except (TypeError, ValueError):
        return None


def probe(path: str) -> Probe:
    """Run ffprobe on ``path`` (reads only)."""
    command = ["ffprobe", "-v", "error", "-show_entries",
               "format=duration,bit_rate:format_tags:stream=codec_type,codec_name,sample_rate,channels:stream_tags",
               "-of", "json", "--", path]
    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=PROBE_TIMEOUT_SECONDS,
                                check=False, stdin=subprocess.DEVNULL)
    except subprocess.TimeoutExpired:
        return Probe(None, None, None, None, None, {}, "ffprobe timed out")
    if result.returncode != 0:
        return Probe(None, None, None, None, None, {}, (result.stderr.strip() or "ffprobe failed")[:300])
    data = json.loads(result.stdout or "{}")
    audio = next((s for s in data.get("streams", []) if s.get("codec_type") == "audio"), None)
    if audio is None:
        return Probe(None, None, None, None, None, {}, "no audio stream")
    fmt = data.get("format", {})
    tags = {str(k).casefold(): str(v) for k, v in {**audio.get("tags", {}), **fmt.get("tags", {})}.items()}
    duration_raw = fmt.get("duration")
    return Probe(
        duration=float(duration_raw) if duration_raw not in (None, "N/A") else None,
        codec=str(audio.get("codec_name") or "") or None,
        sample_rate=_int(audio.get("sample_rate")),
        channels=_int(audio.get("channels")),
        bit_rate=_int(fmt.get("bit_rate")),
        tags=tags,
    )


def audio_hash(path: str) -> str | None:
    """SHA-256 over the first audio stream's packets (tags and container excluded)."""
    command = ["ffmpeg", "-nostdin", "-v", "error", "-i", path, "-map", "0:a:0", "-c", "copy",
               "-f", "hash", "-hash", "sha256", "-"]
    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=HASH_TIMEOUT_SECONDS,
                                check=False, stdin=subprocess.DEVNULL)
    except subprocess.TimeoutExpired:
        log.warning("audio hash timed out path=%s", path)
        return None
    line = result.stdout.strip()
    if result.returncode != 0 or not line.startswith("SHA256="):
        log.warning("audio hash failed path=%s err=%s", path, result.stderr.strip()[:200])
        return None
    return line.removeprefix("SHA256=")


# Decodes that exited 0 without any usable progress time, so a run can report how many (a count only, never a path).
_no_progress_decodes = 0
_CLOCK = re.compile(r"(\d+):([0-5]\d):([0-5]\d(?:\.\d+)?)")


def no_progress_decode_count() -> int:
    """How many decodes in this process exited 0 but printed no usable progress time."""
    return _no_progress_decodes


def progress_seconds(progress: str) -> tuple[float, str] | None:
    """Decoded seconds and the progress key they came from, read from ``-progress`` output; None when it has none.

    Keys, in order of preference: ``out_time_us`` (microseconds), then ``out_time`` (HH:MM:SS.micro). ``out_time_ms``
    is not read: its unit is not part of ffmpeg's documented progress format, and ``out_time`` carries the same value
    unambiguously on every build seen (a 2018 git build prints ``out_time_ms=5000000`` beside
    ``out_time=00:00:05.000000`` and has no ``out_time_us``). Each key's last value that is a number counts; N/A,
    garbage and absurdly large values are ignored.
    """
    last: dict[str, float] = {}
    for line in progress.splitlines():
        key, _, value = line.partition("=")
        value = value.strip()
        try:
            if key == "out_time" and (clock := _CLOCK.fullmatch(value)):
                hours, minutes, seconds = clock.groups()
                last[key] = int(hours) * 3600 + int(minutes) * 60 + float(seconds)
            elif key == "out_time_us" and value.isascii() and value.isdigit():
                last[key] = int(value) / 1_000_000
        except (ValueError, OverflowError):
            continue
    for key in ("out_time_us", "out_time"):
        if key in last:
            return last[key], key
    return None


def decode_duration(path: str) -> float | None:
    """Seconds of the first audio stream measured by decoding it to null (reads only), not the container's claim."""
    global _no_progress_decodes  # noqa: PLW0603
    command = ["ffmpeg", "-nostdin", "-v", "error", "-progress", "pipe:1", "-nostats", "-i", path, "-map", "0:a:0",
               "-f", "null", "-"]
    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=HASH_TIMEOUT_SECONDS,
                                check=False, stdin=subprocess.DEVNULL)
    except subprocess.TimeoutExpired:
        return None
    if result.returncode != 0:
        return None
    found = progress_seconds(result.stdout)
    if found is None:
        _no_progress_decodes += 1
        return None
    return found[0]
