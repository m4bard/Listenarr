"""APPLY: write a reviewed plan into the database in one transaction. Never touches library files."""

from __future__ import annotations

import json
import logging
import os
import sqlite3
import stat
from dataclasses import dataclass, field
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from listenarr_relink import PLAN_FORMAT_VERSION, TOOL_NAME, identity
from listenarr_relink.inventory import BOOK_COLUMNS, FILE_COLUMNS, lstat_or_none, row_digest
from listenarr_relink.paths import PathMap

log = logging.getLogger("relink.apply")

RELINK_REASON = "Path re-linked offline by listenarr-relink; awaiting identity reconciliation at next start."


class ApplyAborted(Exception):
    """Nothing was written."""


@dataclass
class ApplyOutcome:
    """What happened, for the report and the exit code."""

    applied_rows: list[int] = field(default_factory=list)
    applied_books: list[int] = field(default_factory=list)
    refused: list[dict[str, Any]] = field(default_factory=list)
    undo: list[str] = field(default_factory=list)


def processes_holding(db_file: str) -> tuple[list[int], int]:
    """PIDs with the DB (or its -wal/-shm) open, and how many processes could not be inspected."""
    targets = {os.path.realpath(db_file + suffix) for suffix in ("", "-wal", "-shm", "-journal")}
    holders: list[int] = []
    unreadable = 0
    for entry in os.listdir("/proc"):
        if not entry.isdigit() or int(entry) == os.getpid():
            continue
        fd_dir = f"/proc/{entry}/fd"
        try:
            names = os.listdir(fd_dir)
        except OSError:
            unreadable += 1
            continue
        for name in names:
            try:
                if os.readlink(f"{fd_dir}/{name}") in targets:
                    holders.append(int(entry))
                    break
            except OSError:
                continue
    return holders, unreadable


def _sql_literal(value: object) -> str:
    if value is None:
        return "NULL"
    if isinstance(value, int | float):
        return repr(value)
    return "'" + str(value).replace("'", "''") + "'"


def _current(connection: sqlite3.Connection, table: str, columns: tuple[str, ...],
             row_id: int) -> dict[str, object] | None:
    row = connection.execute(f"SELECT {', '.join(columns)} FROM {table} WHERE Id = ?", (row_id,)).fetchone()
    return dict(row) if row is not None else None


def _check_relink(connection: sqlite3.Connection, entry: dict[str, Any], path_map: PathMap) -> str | None:
    current = _current(connection, "AudiobookFiles", FILE_COLUMNS, int(entry["row_id"]))
    if current is None:
        return "row no longer exists"
    if row_digest(current) != entry["precondition"]["row_sha256"]:
        return "row changed since the plan"
    if lstat_or_none(path_map.to_host(str(entry["old_path"]))) is not None:
        return "old path exists again"
    host_new = path_map.to_host(str(entry["new_path"]))
    info = lstat_or_none(host_new)
    expected = entry["precondition"]["file"]
    if info is None or not stat.S_ISREG(info.st_mode):
        return "target is gone or no longer a regular file"
    if (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns) != (
            expected["dev"], expected["ino"], expected["size"], expected["mtime_ns"]):
        return "target file changed since the plan (device/inode/size/mtime)"
    holder = connection.execute("SELECT Id FROM AudiobookFiles WHERE Path = ? AND Id <> ?",
                                (entry["new_path"], entry["row_id"])).fetchone()
    if holder is not None:
        return f"target path is now held by row {holder[0]}"
    if entry["keep_physical_identity"]:
        stored = identity.parse_stored(str(current.get("PhysicalObjectIdentity") or ""))
        live = identity.read_live(host_new)
        if stored is None or live is None or not live.matches(stored):
            return "physical identity no longer matches the target"
    return None


def apply_plan(db_file: str, plan_path: Path, plan: dict[str, Any], out_dir: Path, dry_run: bool) -> ApplyOutcome:
    """Validate every entry against the live DB and disk; write all of them in one transaction, or none."""
    if plan.get("tool") != TOOL_NAME or plan.get("format") != PLAN_FORMAT_VERSION:
        raise ApplyAborted(f"{plan_path} is not a {TOOL_NAME} format {PLAN_FORMAT_VERSION} plan")
    path_map = PathMap.parse(list(plan.get("path_map", [])))
    targets = [str(e["new_path"]) for e in plan["relinks"]]
    if len(targets) != len(set(targets)):
        raise ApplyAborted("plan links two rows to one file")
    outcome = ApplyOutcome()
    connection = sqlite3.connect(db_file, timeout=0, isolation_level=None)
    connection.row_factory = sqlite3.Row
    try:
        connection.execute("PRAGMA foreign_keys = ON")
        try:
            connection.execute("BEGIN IMMEDIATE")
        except sqlite3.OperationalError as error:
            raise ApplyAborted(f"database is locked by another writer: {error}") from error
        refused_books: set[int] = set()
        for entry in plan["relinks"]:
            reason = _check_relink(connection, entry, path_map)
            if reason is not None:
                outcome.refused.append({"row_id": entry["row_id"], "audiobook_id": entry["audiobook_id"],
                                        "reason": reason})
                refused_books.add(int(entry["audiobook_id"]))
                continue
            before = _current(connection, "AudiobookFiles", (*FILE_COLUMNS, "PathIdentityReason"),
                              int(entry["row_id"]))
            assert before is not None
            keep = bool(entry["keep_physical_identity"])
            cursor = connection.execute(
                "UPDATE AudiobookFiles SET Path = ?, CanonicalPath = NULL, PathIdentityLookupKey = NULL, "
                "PathOwnershipKey = NULL, PathIdentityState = 'Unavailable', PathIdentityReason = ?, "
                "PhysicalObjectIdentity = CASE WHEN ? THEN PhysicalObjectIdentity ELSE NULL END, "
                "PhysicalIdentityObservedAtUtc = CASE WHEN ? THEN PhysicalIdentityObservedAtUtc ELSE NULL END "
                "WHERE Id = ? AND Path = ?",
                (entry["new_path"], RELINK_REASON, keep, keep, entry["row_id"], entry["old_path"]))
            if cursor.rowcount != 1:
                raise ApplyAborted(f"row {entry['row_id']} update touched {cursor.rowcount} rows")
            restore = ", ".join(f"{column} = {_sql_literal(before[column])}" for column in
                                ("Path", "CanonicalPath", "PathIdentityLookupKey", "PathOwnershipKey",
                                 "PathIdentityState", "PathIdentityReason", "PhysicalObjectIdentity",
                                 "PhysicalIdentityObservedAtUtc"))
            outcome.undo.append(f"UPDATE AudiobookFiles SET {restore} "
                                f"WHERE Id = {int(entry['row_id'])} AND Path = {_sql_literal(entry['new_path'])};")
            outcome.applied_rows.append(int(entry["row_id"]))
        for update in plan["book_updates"]:
            book_id = int(update["audiobook_id"])
            if book_id in refused_books:
                outcome.refused.append({"audiobook_id": book_id, "reason": "a re-link of this book was refused"})
                continue
            current = _current(connection, "Audiobooks", BOOK_COLUMNS, book_id)
            if current is None or row_digest(current) != update["precondition"]["row_sha256"]:
                outcome.refused.append({"audiobook_id": book_id, "reason": "audiobook row changed since the plan"})
                continue
            new_base = update["set"].get("BasePath")
            if new_base is not None and not os.path.isdir(path_map.to_host(str(new_base))):
                outcome.refused.append({"audiobook_id": book_id, "reason": "new BasePath folder is gone"})
                continue
            columns = sorted(update["set"])
            connection.execute(f"UPDATE Audiobooks SET {', '.join(f'{c} = ?' for c in columns)} WHERE Id = ?",
                               [*(update["set"][c] for c in columns), book_id])
            outcome.undo.append(f"UPDATE Audiobooks SET "
                                f"{', '.join(f'{c} = {_sql_literal(current[c])}' for c in columns)} "
                                f"WHERE Id = {book_id};")
            outcome.applied_books.append(book_id)
        duplicates = connection.execute(
            "SELECT Path, count(*) FROM AudiobookFiles WHERE Path IS NOT NULL GROUP BY Path HAVING count(*) > 1"
        ).fetchall()
        relinked = {str(e["new_path"]) for e in plan["relinks"] if int(e["row_id"]) in outcome.applied_rows}
        clashes = [str(row[0]) for row in duplicates if str(row[0]) in relinked]
        if clashes:
            connection.execute("ROLLBACK")
            raise ApplyAborted(f"would leave {len(clashes)} paths held by two rows, e.g. {clashes[0]}")
        if outcome.refused:
            # All or nothing: a partial write could leave a multi-file book half re-linked (part live,
            # part stale), the population a recursive book-folder delete is most dangerous on. Re-plan.
            connection.execute("ROLLBACK")
            log.warning("refused %d entries; rolled back, nothing written (would have applied rows=%d books=%d)",
                        len(outcome.refused), len(outcome.applied_rows), len(outcome.applied_books))
            outcome.undo.clear()
        elif dry_run:
            connection.execute("ROLLBACK")
            log.info("dry run: rolled back applied_rows=%d applied_books=%d refused=0",
                     len(outcome.applied_rows), len(outcome.applied_books))
        else:
            connection.execute("COMMIT")
            log.info("committed applied_rows=%d applied_books=%d",
                     len(outcome.applied_rows), len(outcome.applied_books))
    except BaseException:
        if connection.in_transaction:
            connection.execute("ROLLBACK")
        raise
    finally:
        connection.close()
    stamp = datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ")
    kind = "dry-run" if dry_run else "apply"
    (out_dir / f"{kind}-report-{stamp}.json").write_text(json.dumps({
        "plan": str(plan_path), "db": db_file, "dry_run": dry_run, "committed": not dry_run and not outcome.refused,
        "applied_rows": outcome.applied_rows,
        "applied_books": outcome.applied_books, "refused": outcome.refused}, indent=1) + "\n", encoding="utf-8")
    if not dry_run and outcome.undo:
        (out_dir / f"undo-{stamp}.sql").write_text(
            "-- Reverses the re-link written at " + stamp + ". Service STOPPED. Restoring the backup is the\n"
            "-- primary rollback; this is for a targeted undo.\nBEGIN IMMEDIATE;\n" + "\n".join(outcome.undo)
            + "\nCOMMIT;\n", encoding="utf-8")
    return outcome
