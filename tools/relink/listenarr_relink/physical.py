"""Which walked file, if any, is the object a stale row was registered with.

A file is the row's object when its inode and file handle (object number and generation) equal the stored
ones and either its device is the stored device, or it sits on the dataset of the library root it was found
under. The second case covers a device minor that drifted (ZFS gives a dataset a new anonymous minor when it
is mounted again). It never reaches across datasets: the handle carries no dataset id, so a clone or a
received copy of a dataset holds the very same handles.

Refused, never guessed:

* more than one file (or a same-inode file that cannot be read) could be the object: ambiguous;
* the object has more than one name (``nlink > 1``): hard-linked, not linked automatically;
* the only match sits on another dataset or pool, or the stored device is another filesystem under the roots
  now: cross-dataset.

The size is not consulted here: a stored size of 64 is a placeholder, not evidence.
"""

from __future__ import annotations

import os
import re
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import PurePosixPath

from listenarr_relink.identity import LiveIdentity, StoredIdentity
from listenarr_relink.inventory import DiskFile
from listenarr_relink.paths import is_within

MATCH = "identity-match"
NO_STORED = "no-stored-identity"
NO_CANDIDATE = "no-candidate"
HANDLE_MISMATCH = "handle-mismatch"
HANDLE_UNREADABLE = "handle-unreadable"
CROSS_POOL = "cross-pool"
OTHER_DATASET = "other-dataset-same-pool"
AMBIGUOUS_ROWS = "ambiguous-shared-by-rows"
AMBIGUOUS_FILES = "ambiguous-shared-by-files"
HARDLINKED = "hardlinked"
FOLDER_ID_DISAGREES = "folder-id-disagrees"
# Verdicts after which the row may still be matched by size (as before: no object was found for it).
FALL_THROUGH = frozenset({NO_CANDIDATE, HANDLE_MISMATCH, HANDLE_UNREADABLE})

_BRACKETED_ID = re.compile(r"\[([^\[\]]+)\]\s*$")


@dataclass(frozen=True)
class Verdict:
    """The outcome for one stored identity: a kind, the files it concerns and a reason for the plan."""

    kind: str
    hits: tuple[DiskFile, ...] = ()
    device_drift: bool = False
    detail: str = ""


def _cross_kind(pools: dict[int, str], file_dev: int, other_dev: int | None) -> str:
    file_pool = pools.get(file_dev)
    other_pool = pools.get(other_dev) if other_dev is not None else None
    return OTHER_DATASET if file_pool is not None and file_pool == other_pool else CROSS_POOL


def classify(stored: StoredIdentity, same_inode: list[DiskFile], live_of: Callable[[str], LiveIdentity | None],
             root_device_of: Callable[[DiskFile], int | None], devices_under_roots: frozenset[int],
             pools: dict[int, str]) -> Verdict:
    """Decide which of the unclaimed files carrying the stored inode number is the row's object."""
    eligible: list[tuple[DiskFile, bool]] = []
    crossing: list[tuple[DiskFile, str]] = []
    mismatched = 0
    unreadable = 0
    for disk_file in same_inode:
        live = live_of(disk_file.host_path)
        if live is None or os.makedev(live.major, live.minor) != disk_file.dev or live.inode != disk_file.ino:
            unreadable += 1  # unreadable, or replaced since the walk: it might be the object
            continue
        if live.matches(stored):
            eligible.append((disk_file, False))
        elif not live.same_object(stored):
            mismatched += 1
        elif stored.device in devices_under_roots:
            # The stored device is another filesystem under the roots now: the row was registered there.
            crossing.append((disk_file, _cross_kind(pools, disk_file.dev, stored.device)))
        elif disk_file.dev != (root_dev := root_device_of(disk_file)):
            crossing.append((disk_file, _cross_kind(pools, disk_file.dev, root_dev)))
        else:
            eligible.append((disk_file, True))
    hits = [f for f, _ in eligible] + [f for f, _ in crossing]
    if not hits:
        if mismatched:
            return Verdict(HANDLE_MISMATCH, detail="inode found but its file handle differs (inode reused)")
        if unreadable:
            return Verdict(HANDLE_UNREADABLE, detail="inode found but its file handle could not be read")
        return Verdict(NO_CANDIDATE)
    if len(hits) > 1 or unreadable:
        if not unreadable and len({(f.dev, f.ino) for f in hits}) == 1:
            return Verdict(HARDLINKED, tuple(hits),
                           detail=f"physical identity matches {len(hits)} names of one object (hard links)")
        return Verdict(AMBIGUOUS_FILES, tuple(hits),
                       detail=f"physical identity is carried by {len(hits)} files, {unreadable} more could not "
                              "be read (ambiguous, not linked)")
    if crossing:
        kind = crossing[0][1]
        where = "another pool" if kind == CROSS_POOL else "another dataset of the same pool"
        return Verdict(kind, tuple(hits), detail=f"physical identity matched only a file on {where} (refused: "
                                                 "only the stored device or the library root's dataset counts)")
    (hit, drift), = eligible
    if hit.nlink > 1:
        return Verdict(HARDLINKED, (hit,), drift,
                       detail=f"physical identity matches a file with {hit.nlink} hard links; not linked "
                              "automatically")
    return Verdict(MATCH, (hit,), drift)


def folder_id(db_path: str, root: str | None) -> str | None:
    """The bracketed id ending the nearest folder name (below ``root``) that has one, casefolded."""
    parent = PurePosixPath(db_path).parent
    while root is None or (is_within(str(parent), root) and str(parent).rstrip("/") != root.rstrip("/")):
        found = _BRACKETED_ID.search(parent.name)
        if found:
            return found.group(1).strip().casefold()
        if parent == parent.parent:
            return None
        parent = parent.parent
    return None
