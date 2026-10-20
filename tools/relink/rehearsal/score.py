#!/usr/bin/env python3
"""Score a relink plan against fixture ground truth; the count that matters is wrong links.

usage: score.py <plan.json> <expected.json | layout.json> <db the plan was made from>
  expected.json  adversarial fixture: one entry per row id with expect/truth_path
  layout.json    FF fixture: per book stored[]/real[]; truth is derived by matching the row's stored Path
exit: 0 no wrong links, 1 at least one wrong link, 2 usage
"""

from __future__ import annotations

import json
import sqlite3
import sys
from collections import Counter
from typing import Any


def truth_from_layout(layout: list[dict[str, Any]], db: str) -> dict[int, dict[str, Any]]:
    """Map row id -> {expect, truth_path} from an FF layout and the plan's DB."""
    real_by_stored = {stored: real for book in layout if book["library"]
                      for stored, real in zip(book["stored"], book["real"], strict=True)}
    connection = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    truth: dict[int, dict[str, Any]] = {}
    for row_id, path in connection.execute("SELECT Id, Path FROM AudiobookFiles"):
        real = real_by_stored.get(path)
        truth[row_id] = {"expect": "untouched" if real == path else "link", "truth_path": real, "case": "ff"}
    return truth


def main() -> int:
    """Entry point."""
    if len(sys.argv) != 4:
        print(__doc__, file=sys.stderr)
        return 2
    with open(sys.argv[1], encoding="utf-8") as handle:
        plan = json.load(handle)
    with open(sys.argv[2], encoding="utf-8") as handle:
        raw = json.load(handle)
    truth = {e["row_id"]: e for e in raw} if raw and "row_id" in raw[0] else truth_from_layout(raw, sys.argv[3])
    linked = {e["row_id"]: e for e in plan["relinks"]}
    left = {e["row_id"]: e for e in plan["left_out"]}
    tally: Counter[str] = Counter()
    lines: list[str] = []
    for row_id in sorted(truth):
        expected = truth[row_id]
        acceptable = set(expected.get("acceptable") or [expected["expect"]])
        label = f"row {row_id} [{expected.get('key') or expected.get('case')}] expect={expected['expect']}"
        if row_id in linked:
            got = linked[row_id]["new_path"]
            if got != expected.get("truth_path"):
                tally["WRONG-LINK"] += 1
                lines.append(f"WRONG-LINK {label} got {got} truth {expected.get('truth_path')}")
            elif "link" not in acceptable:
                tally["link-where-leave-out-expected"] += 1
                lines.append(f"UNEXPECTED-LINK (right content) {label} via {linked[row_id]['via']}")
            else:
                tally["correct-link"] += 1
        elif row_id in left:
            if "leave-out" in acceptable:
                tally["correct-leave-out"] += 1
            else:
                tally["missed-link"] += 1
                lines.append(f"MISSED {label}: {left[row_id]['reason']}")
        else:
            key = "correct-untouched" if expected["expect"] == "untouched" else "not-in-plan"
            tally[key] += 1
            if key != "correct-untouched":
                lines.append(f"NOT-IN-PLAN {label}")
    for extra in sorted(set(linked) - set(truth)):
        tally["WRONG-LINK"] += 1
        lines.append(f"WRONG-LINK row {extra} not in truth")
    print(json.dumps(dict(sorted(tally.items()))))
    print("\n".join(lines))
    return 1 if tally["WRONG-LINK"] else 0


if __name__ == "__main__":
    sys.exit(main())
