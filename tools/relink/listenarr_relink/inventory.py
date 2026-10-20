"""Read-only inventory: the database rows that matter and the audio files under each root."""

from __future__ import annotations

import errno
import hashlib
import json
import logging
import os
import sqlite3
import stat
from dataclasses import dataclass, field

from listenarr_relink.paths import PathMap, is_audio

log = logging.getLogger("relink.inventory")

# Columns whose values a re-link depends on; their digest is the per-row precondition APPLY re-checks.
FILE_COLUMNS = ("Id", "AudiobookId", "Path", "Size", "DurationSeconds", "Codec", "SampleRate", "Channels",
                "PhysicalObjectIdentity", "PhysicalIdentityObservedAtUtc", "CanonicalPath", "PathIdentityState",
                "PathIdentityLookupKey", "PathOwnershipKey")
BOOK_COLUMNS = ("Id", "Title", "Authors", "BasePath", "FilePath")


@dataclass
class FileRow:
    """One AudiobookFiles row."""

    values: dict[str, object]

    @property
    def id(self) -> int:
        """Row id."""
        return int(str(self.values["Id"]))

    @property
    def audiobook_id(self) -> int:
        """Owning audiobook id."""
        return int(str(self.values["AudiobookId"]))

    @property
    def path(self) -> str:
        """Stored path ('' when NULL)."""
        return str(self.values["Path"] or "")

    @property
    def size(self) -> int | None:
        """Stored size, None when NULL."""
        value = self.values["Size"]
        return None if value is None else int(str(value))

    def digest(self) -> str:
        """Stable digest of the precondition columns."""
        return row_digest(self.values)


@dataclass
class BookRow:
    """One Audiobooks row (the columns the re-link reads or writes)."""

    values: dict[str, object]

    @property
    def id(self) -> int:
        """Audiobook id."""
        return int(str(self.values["Id"]))

    @property
    def title(self) -> str:
        """Title ('' when NULL)."""
        return str(self.values["Title"] or "")

    @property
    def authors(self) -> list[str]:
        """Authors parsed from the JSON column."""
        raw = self.values["Authors"]
        try:
            parsed = json.loads(str(raw)) if raw else []
        except json.JSONDecodeError:
            return [str(raw)]
        return [str(a) for a in parsed] if isinstance(parsed, list) else [str(parsed)]

    @property
    def base_path(self) -> str:
        """BasePath ('' when NULL)."""
        return str(self.values["BasePath"] or "")

    @property
    def file_path(self) -> str:
        """Legacy FilePath ('' when NULL)."""
        return str(self.values["FilePath"] or "")

    def digest(self) -> str:
        """Stable digest of the precondition columns."""
        return row_digest(self.values)


@dataclass(frozen=True)
class DiskFile:
    """A regular audio file found under a root."""

    db_path: str
    host_path: str
    size: int
    dev: int
    ino: int
    nlink: int
    mtime_ns: int
    ctime_ns: int


@dataclass
class Library:
    """Everything PLAN reads."""

    roots: list[str]
    books: dict[int, BookRow]
    files: list[FileRow]
    disk: list[DiskFile] = field(default_factory=list)
    symlinks: list[str] = field(default_factory=list)
    walk_errors: list[str] = field(default_factory=list)


def row_digest(values: dict[str, object]) -> str:
    """SHA-256 over a JSON rendering of the row values (key order fixed)."""
    rendered = json.dumps({k: values[k] for k in sorted(values)}, sort_keys=True, default=str)
    return hashlib.sha256(rendered.encode("utf-8")).hexdigest()


def open_read_only(db_file: str) -> sqlite3.Connection:
    """Open a SQLite file strictly read-only."""
    connection = sqlite3.connect(f"file:{db_file}?mode=ro", uri=True)
    connection.row_factory = sqlite3.Row
    return connection


def snapshot_database(live_db: str, copy_path: str) -> None:
    """Consistent copy of ``live_db`` through SQLite's backup API, reading the source read-only."""
    source = open_read_only(live_db)
    try:
        target = sqlite3.connect(copy_path)
        try:
            source.backup(target)
        finally:
            target.close()
    finally:
        source.close()


def load_rows(connection: sqlite3.Connection) -> tuple[list[str], dict[int, BookRow], list[FileRow]]:
    """Roots, audiobooks and file rows."""
    roots = [str(r["Path"]) for r in connection.execute("SELECT Path FROM RootFolders ORDER BY Id") if r["Path"]]
    books = {
        int(r["Id"]): BookRow(dict(r))
        for r in connection.execute(f"SELECT {', '.join(BOOK_COLUMNS)} FROM Audiobooks")
    }
    files = [FileRow(dict(r)) for r in
             connection.execute(f"SELECT {', '.join(FILE_COLUMNS)} FROM AudiobookFiles ORDER BY Id")]
    return roots, books, files


def lstat_or_none(host_path: str) -> os.stat_result | None:
    """``lstat`` that returns None only for 'does not exist'; other errors propagate."""
    try:
        return os.lstat(host_path)
    except OSError as error:
        if error.errno in (errno.ENOENT, errno.ENOTDIR):
            return None
        raise


class PathMapError(ValueError):
    """The path map does not translate a walked file back to the same host path."""


def walk_root(db_root: str, path_map: PathMap, library: Library) -> None:
    """Collect regular audio files under one root without following links.

    Raises PathMapError when a file's DB spelling would not translate back to the file itself.
    """
    host_root = path_map.to_host(db_root)
    count = 0

    def on_error(error: OSError) -> None:
        library.walk_errors.append(f"{error.filename}: {error.strerror}")

    for directory, dirnames, filenames in os.walk(host_root, onerror=on_error, followlinks=False):
        for name in [*dirnames]:
            if os.path.islink(os.path.join(directory, name)):
                library.symlinks.append(path_map.to_db(os.path.join(directory, name)))
        for name in filenames:
            host_path = os.path.join(directory, name)
            try:
                info = os.lstat(host_path)
            except OSError as error:
                library.walk_errors.append(f"{host_path}: {error.strerror}")
                continue
            if stat.S_ISLNK(info.st_mode):
                library.symlinks.append(path_map.to_db(host_path))
                continue
            if not stat.S_ISREG(info.st_mode) or not is_audio(name):
                continue
            db_path = path_map.to_db(host_path)
            if path_map.to_host(db_path) != host_path:
                raise PathMapError(f"path map sends {host_path} to {db_path}, which maps back to "
                                   f"{path_map.to_host(db_path)}; fix overlapping --path-map prefixes")
            library.disk.append(DiskFile(db_path, host_path, info.st_size, info.st_dev, info.st_ino,
                                         info.st_nlink, info.st_mtime_ns, info.st_ctime_ns))
            count += 1
    log.info("walked root=%s audio_files=%d", db_root, count)
