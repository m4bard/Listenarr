"""Read-only audio evidence via ffprobe (stream facts, tags) and ffmpeg (audio-stream hash)."""

from __future__ import annotations

import json
import logging
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
