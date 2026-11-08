"""Re-point a quarantined book's stale rows at the copy of its audio the library already holds (``in-root-copy``).

Nothing moves on disk. The plan is an ordinary PLAN-format document, so the reviewed ``apply`` writes it (one
transaction, per-row preconditions, undo SQL) and ``verify`` / the APPLY packet's checks read it. Each relink
points a stale row whose object sits in the quarantine at the in-root file with the same audio-stream hash and
duration (``qrestore._copy_pairs``). The stored physical identity names the quarantined object, not the copy, so
it is cleared, as for a drifted device.

A book is refused (and with it the whole plan, so the operator re-chooses) unless:

* its class is exactly ``in-root-copy``, and none of its failing checks is a content check
  (``qrestore.CONTENT_CHECKS``) or ``not-every-row-found``;
* every stale row of the book is paired with a copy (no row under the root left to PLAN, none left over);
* no copy is a path any row stores, or an object a live row reaches, and no copy is chosen twice.

``verify_copy_plan`` re-hashes every copy (and every quarantined original still present) just before APPLY and
refuses on any difference: the audio identity the plan rests on is checked again, not assumed from the pin.
"""

from __future__ import annotations

from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from listenarr_relink import PLAN_FORMAT_VERSION, TOOL_NAME, media
from listenarr_relink.inventory import Library, lstat_or_none
from listenarr_relink.matcher import _resolves
from listenarr_relink.paths import PathMap
from listenarr_relink.plan_io import file_sha256
from listenarr_relink.qrestore import CONTENT_CHECKS, Survey

COPY_CLASS = "in-root-copy"
CONFIDENCE = "audio-identity-copy"


class CopyPlanRefused(ValueError):
    """A chosen book cannot be re-pointed; nothing is written."""


def build_copy_plan(result: Survey, chosen: frozenset[int], library: Library, path_map: PathMap, db_copy: Path,
                    live_db: str) -> dict[str, Any]:
    """The relink plan for the operator's chosen in-root-copy books; CopyPlanRefused lists every refusal."""
    by_id = {b.book.id: b for b in result.books}
    row_paths = {row.path for row in library.files if row.path}
    live_objects: set[tuple[int, int]] = set()
    stale_by_book: dict[int, set[int]] = {}
    for row in library.files:
        if not row.path:
            continue
        if _resolves(row.path, path_map):
            info = lstat_or_none(path_map.to_host(row.path))
            if info is not None:
                live_objects.add((info.st_dev, info.st_ino))
        else:
            stale_by_book.setdefault(row.audiobook_id, set()).add(row.id)
    refusals: list[str] = []
    used: set[tuple[int, int]] = set()
    relinks: list[dict[str, Any]] = []
    book_updates: list[dict[str, Any]] = []
    rows_by_id = {row.id: row for row in library.files}
    for book_id in sorted(chosen):
        restore = by_id.get(book_id)
        if restore is None or restore.verdict != COPY_CLASS:
            refusals.append(f"audiobook {book_id}: class is {restore.verdict if restore else 'no candidate'}, "
                            f"not {COPY_CLASS}")
            continue
        blocking = [f for f in restore.failures if f in (*CONTENT_CHECKS, "not-every-row-found")]
        if blocking:
            refusals.append(f"audiobook {book_id}: fails {', '.join(blocking)} under its copy")
            continue
        paired_rows = {item.row_id for item, _, _ in restore.copy_pairs if item.row_id is not None}
        if paired_rows != stale_by_book.get(book_id, set()):
            refusals.append(f"audiobook {book_id}: {len(stale_by_book.get(book_id, set()) - paired_rows)} stale "
                            "row(s) without a paired copy")
            continue
        for item, copy, audio in restore.copy_pairs:
            if item.row_id is None:
                continue
            key = (copy.dev, copy.ino)
            if copy.db_path in row_paths or key in live_objects:
                refusals.append(f"audiobook {book_id}: a copy is already tracked by a row")
                break
            if key in used:
                refusals.append(f"audiobook {book_id}: a copy is chosen twice")
                break
            used.add(key)
            row = rows_by_id[item.row_id]
            relinks.append({
                "row_id": row.id,
                "audiobook_id": book_id,
                "book_title": restore.book.title,
                "old_path": row.path,
                "new_path": copy.db_path,
                "host_new_path": copy.host_path,
                "confidence": CONFIDENCE,
                "via": "the in-root file has the same audio-stream hash and duration as the row's quarantined object",
                "keep_physical_identity": False,
                "evidence": {
                    "copy_of_quarantined": True,
                    "quarantined_path": item.source.host_path,
                    "quarantined_dev": item.source.dev,
                    "quarantined_ino": item.source.ino,
                    "audio_sha256": audio,
                    "stored_duration": row.values.get("DurationSeconds"),
                    "file_duration": item.probe.duration if item.probe else None,
                    "label": restore.label,
                },
                "precondition": {
                    "row_sha256": row.digest(),
                    "file": {"dev": copy.dev, "ino": copy.ino, "size": copy.size, "mtime_ns": copy.mtime_ns,
                             "ctime_ns": copy.ctime_ns, "nlink": copy.nlink},
                },
            })
        if restore.target and restore.book.base_path.rstrip("/") != restore.target:
            book_updates.append({"audiobook_id": book_id, "book_title": restore.book.title,
                                 "set": {"BasePath": restore.target}, "was": {"BasePath": restore.book.base_path},
                                 "precondition": {"row_sha256": restore.book.digest()}})
    if refusals:
        raise CopyPlanRefused("; ".join(refusals))
    return {
        "tool": TOOL_NAME,
        "format": PLAN_FORMAT_VERSION,
        "kind": "in-root-copy-relink",
        "created_utc": datetime.now(UTC).isoformat(timespec="seconds"),
        "source_db": live_db,
        "db_copy": {"path": str(db_copy), "sha256": file_sha256(db_copy)},
        "roots": library.roots,
        "path_map": [f"{a}={b}" for a, b in path_map.pairs],
        "restore_dirs": library.restore_dirs,
        "summary": {"books": len(chosen), "relinks": len(relinks), "book_updates": len(book_updates)},
        "relinks": relinks,
        "book_updates": book_updates,
        "left_out": [],
        "flags": [],
        "walk_errors": library.walk_errors,
    }


def verify_copy_plan(plan: dict[str, Any]) -> tuple[int, list[str]]:
    """Re-hash every copy (and quarantined original still present); (checked, mismatches as counts-only text)."""
    if plan.get("kind") != "in-root-copy-relink":
        raise CopyPlanRefused("not an in-root-copy relink plan")
    mismatches: list[str] = []
    checked = 0
    for entry in plan["relinks"]:
        want = entry["evidence"]["audio_sha256"]
        checked += 1
        if media.audio_hash(str(entry["host_new_path"])) != want:
            mismatches.append("copy audio differs from the plan")
        original = str(entry["evidence"]["quarantined_path"])
        if lstat_or_none(original) is not None and media.audio_hash(original) != want:
            mismatches.append("quarantined original's audio differs from the plan")
    return checked, mismatches
