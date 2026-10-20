"""Path translation between the service's view (what the DB stores) and this host, plus text tokens."""

from __future__ import annotations

import os
import re
import unicodedata
from dataclasses import dataclass
from pathlib import PurePosixPath

# Listenarr's FileUtils.AudioExtensions plus the containers it treats as audio some of the time.
AUDIO_EXTENSIONS = frozenset({
    ".m4b", ".mp3", ".flac", ".ogg", ".opus", ".m4a", ".aac", ".wav",
    ".wv", ".wma", ".ape", ".alac", ".aif", ".aiff", ".mp4", ".mka", ".webm",
})

_TOKEN = re.compile(r"[^\W_]+")
_LAST_NUMBER = re.compile(r"(\d+)(?!.*\d)")


@dataclass(frozen=True)
class PathMap:
    """Ordered prefix pairs mapping service paths (as stored in the DB) to host paths and back."""

    pairs: tuple[tuple[str, str], ...] = ()

    @staticmethod
    def parse(specs: list[str]) -> PathMap:
        """Build from ``DBPREFIX=HOSTPREFIX`` strings; longest DB prefix wins."""
        pairs: list[tuple[str, str]] = []
        for spec in specs:
            db_prefix, sep, host_prefix = spec.partition("=")
            if not sep or not db_prefix.startswith("/") or not host_prefix.startswith("/"):
                raise ValueError(f"path map must be ABSOLUTE_DB_PREFIX=ABSOLUTE_HOST_PREFIX, got {spec!r}")
            pairs.append((db_prefix.rstrip("/") or "/", host_prefix.rstrip("/") or "/"))
        pairs.sort(key=lambda pair: len(pair[0]), reverse=True)
        return PathMap(tuple(pairs))

    def to_host(self, db_path: str) -> str:
        """Translate a stored path to the path this host sees."""
        return _swap(db_path, self.pairs, 0)

    def to_db(self, host_path: str) -> str:
        """Translate a host path back to the spelling the service stores."""
        return _swap(host_path, tuple(sorted(self.pairs, key=lambda p: len(p[1]), reverse=True)), 1)


def _swap(path: str, pairs: tuple[tuple[str, str], ...], side: int) -> str:
    for pair in pairs:
        source, target = pair[side], pair[1 - side]
        if path == source:
            return target
        if path.startswith(source.rstrip("/") + "/"):
            return target.rstrip("/") + path[len(source.rstrip("/")):]
    return path


def is_within(path: str, directory: str) -> bool:
    """True when ``path`` equals ``directory`` or lies beneath it (lexically)."""
    directory = directory.rstrip("/") or "/"
    return path == directory or path.startswith(directory + "/") or directory == "/"


def is_audio(path: str) -> bool:
    """True for extensions Listenarr can treat as audio."""
    return os.path.splitext(path)[1].lower() in AUDIO_EXTENSIONS


def tokens(text: str) -> list[str]:
    """Accent-folded, case-folded word tokens in any script (combining marks dropped, letters kept)."""
    decomposed = unicodedata.normalize("NFKD", text)
    folded = "".join(ch for ch in decomposed if not unicodedata.combining(ch)).casefold()
    return _TOKEN.findall(folded)


def is_distinctive(title: str) -> bool:
    """True when a title has a token that can identify it: 3+ letters, or 2+ non-ASCII letters (CJK).

    A title that folds to digits or short words (``1``, ``It``) is never evidence and never a veto key.
    """
    return any(not token.isdigit() and (len(token) >= 3 or (len(token) >= 2 and not token.isascii()))
               for token in tokens(title))


def coverage(needle: str, haystack_tokens: set[str]) -> float:
    """Fraction of ``needle``'s distinct tokens present in ``haystack_tokens`` (0 when needle has none)."""
    needle_tokens = set(tokens(needle))
    if not needle_tokens:
        return 0.0
    return len(needle_tokens & haystack_tokens) / len(needle_tokens)


def part_number(path: str) -> int | None:
    """Last run of digits in the file stem, e.g. ``Title-02.mp3`` -> 2."""
    match = _LAST_NUMBER.search(PurePosixPath(path).stem)
    return int(match.group(1)) if match else None


def path_tokens(path: str, root: str) -> set[str]:
    """Tokens of the directories below ``root`` and the file stem."""
    relative = PurePosixPath(path).relative_to(root) if is_within(path, root) else PurePosixPath(path)
    parts = [*relative.parent.parts, relative.stem]
    return {token for part in parts for token in tokens(part)}
