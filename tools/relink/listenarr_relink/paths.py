"""Path translation between the service's view (what the DB stores) and this host, plus text tokens."""

from __future__ import annotations

import errno
import os
import re
import stat
import unicodedata
from dataclasses import dataclass
from pathlib import PurePosixPath

# Listenarr's FileUtils.AudioExtensions plus the containers it treats as audio some of the time.
AUDIO_EXTENSIONS = frozenset({
    ".m4b", ".mp3", ".flac", ".ogg", ".opus", ".m4a", ".aac", ".wav",
    ".wv", ".wma", ".ape", ".alac", ".aif", ".aiff", ".mp4", ".mka", ".webm",
})

_TOKEN = re.compile(r"[^\W_]+")
# Words a file or folder name may add to a title without naming another work (dropped unless in the title).
NOISE_TOKENS = frozenset({"part", "pt", "disc", "disk", "cd", "track", "chapter", "unabridged", "abridged",
                          "audiobook", "audio", "mp3", "m4b"})
MAX_SYMLINK_HOPS = 40


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


def names_title(text: str, title: str, authors: list[str]) -> bool:
    """True when ``text`` names exactly this title and nothing more.

    Its word tokens, minus numbers, the authors' names and noise words (``part``, ``disc``...), must equal
    the title's tokens minus numbers. A sibling or containing title (``The Voyages of Doctor Dolittle`` for
    ``The Story of Doctor Dolittle``, ``Tanar of Pellucidar`` for ``Pellucidar``) is not this title.
    """
    if not is_distinctive(title):
        return False
    title_tokens = {t for t in tokens(title) if not t.isdigit()}
    author_tokens = {t for author in authors for t in tokens(author)} - title_tokens
    rest = {t for t in tokens(text) if not t.isdigit()} - author_tokens - (NOISE_TOKENS - title_tokens)
    return rest == title_tokens


def name_parts(path: str, root: str) -> list[str]:
    """The directory names below ``root`` and the file stem."""
    relative = PurePosixPath(path).relative_to(root) if is_within(path, root) else PurePosixPath(path)
    return [*relative.parent.parts, relative.stem]


def resolve_host(host_path: str, path_map: PathMap) -> str | None:
    """Follow symlinks in ``host_path`` as the service would, returning the host path of the final object.

    Absolute link text is in the service's namespace and goes through the path map. Returns None when a
    component does not exist (a dangling link); raises OSError for anything else (permission, loops).
    """
    resolved = "/"
    pending = list(PurePosixPath(host_path).parts[1:])
    hops = 0
    while pending:
        name = pending.pop(0)
        if name in ("", "."):
            continue
        if name == "..":
            resolved = os.path.dirname(resolved) or "/"
            continue
        candidate = os.path.join(resolved, name)
        try:
            info = os.lstat(candidate)
        except OSError as error:
            if error.errno in (errno.ENOENT, errno.ENOTDIR):
                return None
            raise
        if not stat.S_ISLNK(info.st_mode):
            resolved = candidate
            continue
        hops += 1
        if hops > MAX_SYMLINK_HOPS:
            raise OSError(errno.ELOOP, "too many symlinks", host_path)
        target = os.readlink(candidate)
        if target.startswith("/"):
            resolved = "/"
            pending = [*PurePosixPath(path_map.to_host(target)).parts[1:], *pending]
        else:
            pending = [*PurePosixPath(target).parts, *pending]
    return resolved
