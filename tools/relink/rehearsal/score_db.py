#!/usr/bin/env python3
"""End-state score of a fixture DB after re-linking: which rows point at the right file.

Works whichever mechanism wrote the row (scan rows get new ids), so it scores per audiobook:
every live row must hold a path from that book's truth set (its own files, whatever the fixture
expected the tool to do with them); every truth path expected to be linked should be held. A book
holding its own file where the fixture conservatively expected a leave-out is right content and is
reported separately (held-own-file-where-leave-out-expected), never as a wrong row. A live row the
tool leaves untouched by design but whose stored path now holds other content (C7: the old path was
reused by another book) is reported as PREEXISTING-WRONG-LIVE-ROW: not created by the re-link, but a
delete hazard the plan must flag.

usage: score_db.py <db> <expected.json | layout.json>
exit: 0 no wrong rows, 1 wrong rows found, 2 usage
"""

from __future__ import annotations

import json
import os
import sqlite3
import sys
from collections import Counter, defaultdict


def main() -> int:
    """Entry point."""
    if len(sys.argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    db, truth_file = sys.argv[1], sys.argv[2]
    with open(truth_file, encoding="utf-8") as handle:
        raw = json.load(handle)
    connection = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    truth: dict[int, set[str]] = defaultdict(set)   # book -> its own files (acceptable live paths)
    wanted: dict[int, set[str]] = defaultdict(set)  # book -> paths that should be held
    leave_out: dict[int, set[str]] = defaultdict(set)  # book -> own files the fixture expected left out
    preexisting: dict[int, set[str]] = defaultdict(set)  # book -> live stored paths holding other content
    if raw and "row_id" in raw[0]:
        for entry in raw:
            book = int(entry["audiobook_id"])
            if entry["expect"] == "untouched" and entry["truth_path"] not in (None, entry["stored_path"]):
                preexisting[book].add(entry["stored_path"])
                truth[book].add(entry["truth_path"])
            elif entry["expect"] == "untouched":
                truth[book].add(entry["stored_path"])
                wanted[book].add(entry["stored_path"])
            elif entry["truth_path"]:
                acceptable = set(entry.get("acceptable") or [entry["expect"]])
                truth[book].add(entry["truth_path"])
                if entry["expect"] == "link":
                    wanted[book].add(entry["truth_path"])
                elif "link" not in acceptable:
                    leave_out[book].add(entry["truth_path"])
    else:
        book_by_title = {str(t): int(i) for i, t in connection.execute("SELECT Id, Title FROM Audiobooks")}
        asin_to_id = {str(a): int(i) for i, a in connection.execute("SELECT Id, Asin FROM Audiobooks")}
        for book in raw:
            if not book["library"]:
                continue
            book_id = asin_to_id.get(book["asin"]) or book_by_title.get(book["title"])
            if book_id is None:
                continue
            for real in book["real"]:
                truth[book_id].add(real)
                wanted[book_id].add(real)
    tally: Counter[str] = Counter()
    lines: list[str] = []
    held: dict[int, set[str]] = defaultdict(set)
    for row_id, book_id, path, physical in connection.execute(
            "SELECT Id, AudiobookId, Path, PhysicalObjectIdentity FROM AudiobookFiles"):
        held[int(book_id)].add(str(path))
        if not os.path.lexists(str(path)):
            tally["stale-row"] += 1
            continue
        if str(path) in preexisting[int(book_id)]:
            tally["PREEXISTING-WRONG-LIVE-ROW"] += 1
            lines.append(f"PREEXISTING-WRONG-LIVE-ROW row {row_id} book {book_id} -> {path} (untouched by design)")
        elif str(path) in truth[int(book_id)]:
            tally["right-row"] += 1
            tally["right-row-with-identity" if physical else "right-row-identity-null"] += 1
        else:
            tally["WRONG-ROW"] += 1
            lines.append(f"WRONG-ROW row {row_id} book {book_id} -> {path}")
    for book_id, paths in wanted.items():
        for path in paths - held[book_id]:
            tally["not-held"] += 1
            lines.append(f"not held: book {book_id} {path}")
    for book_id, paths in leave_out.items():
        for path in paths & held[book_id]:
            tally["held-own-file-where-leave-out-expected"] += 1
            lines.append(f"held own file though leave-out expected (right content): book {book_id} {path}")
    print(json.dumps(dict(sorted(tally.items()))))
    print("\n".join(lines))
    return 1 if tally["WRONG-ROW"] else 0


if __name__ == "__main__":
    sys.exit(main())
