"""VERIFY: read-only health report of AudiobookFiles against disk, optionally against a plan."""

from __future__ import annotations

import os
import stat
from collections import Counter, defaultdict
from typing import Any

from listenarr_relink import identity
from listenarr_relink.inventory import lstat_or_none, open_read_only
from listenarr_relink.paths import PathMap


def verify(db_file: str, path_map: PathMap, plan: dict[str, Any] | None) -> dict[str, Any]:
    """Count live/stale rows, identity coverage and duplicates; check a plan's targets if given."""
    connection = open_read_only(db_file)
    try:
        rows = [dict(r) for r in connection.execute(
            "SELECT Id, AudiobookId, Path, Size, PathIdentityState, PhysicalObjectIdentity FROM AudiobookFiles")]
        books = [dict(r) for r in connection.execute("SELECT Id, BasePath FROM Audiobooks")]
    finally:
        connection.close()
    status: Counter[str] = Counter()
    identity_state: Counter[str] = Counter()
    by_object: dict[tuple[int, int], list[int]] = defaultdict(list)
    by_path: dict[str, list[int]] = defaultdict(list)
    stale_ids: list[int] = []
    for row in rows:
        identity_state[str(row["PathIdentityState"])] += 1
        path = str(row["Path"] or "")
        if not path:
            status["empty-path"] += 1
            continue
        by_path[path].append(int(row["Id"]))
        try:
            info = lstat_or_none(path_map.to_host(path))
        except OSError:
            status["unreadable"] += 1
            continue
        if info is None:
            status["stale"] += 1
            stale_ids.append(int(row["Id"]))
            continue
        if stat.S_ISLNK(info.st_mode):
            status["live-symlink"] += 1
            continue
        status["live"] += 1
        by_object[(info.st_dev, info.st_ino)].append(int(row["Id"]))
        stored = identity.parse_stored(str(row["PhysicalObjectIdentity"] or ""))
        if row["PhysicalObjectIdentity"] is None:
            status["live-identity-null"] += 1
        elif stored is not None:
            live = identity.read_live(path_map.to_host(path))
            status["live-identity-match" if live and live.matches(stored) else "live-identity-mismatch"] += 1
    report: dict[str, Any] = {
        "rows": len(rows),
        "status": dict(sorted(status.items())),
        "path_identity_state": dict(sorted(identity_state.items())),
        "rows_sharing_a_path": {p: ids for p, ids in by_path.items() if len(ids) > 1},
        "rows_sharing_a_file_object": [ids for ids in by_object.values() if len(ids) > 1],
        "books_with_missing_basepath": sorted(
            int(b["Id"]) for b in books if b["BasePath"] and not os.path.isdir(path_map.to_host(str(b["BasePath"])))),
        "stale_row_ids": stale_ids,
    }
    if plan is not None:
        current_paths = {int(r["Id"]): str(r["Path"] or "") for r in rows}
        paths_by_book: dict[int, set[str]] = defaultdict(set)
        for row in rows:
            paths_by_book[int(row["AudiobookId"])].add(str(row["Path"] or ""))
        outcome: Counter[str] = Counter()
        mismatches: list[dict[str, Any]] = []
        for entry in plan["relinks"]:
            row_id, new_path = int(entry["row_id"]), str(entry["new_path"])
            if current_paths.get(row_id) == new_path:
                outcome["row-relinked-in-place"] += 1
            elif new_path in paths_by_book[int(entry["audiobook_id"])]:
                outcome["book-holds-target-in-another-row"] += 1
            else:
                outcome["target-not-held"] += 1
                mismatches.append({"row_id": row_id, "audiobook_id": entry["audiobook_id"], "expected": new_path,
                                   "current": current_paths.get(row_id)})
        report["plan_check"] = {"outcome": dict(sorted(outcome.items())), "not_held": mismatches}
    return report
