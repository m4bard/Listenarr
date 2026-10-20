#!/usr/bin/env python3
"""Summarise JJ delete runs: per run, the delete response and the tree diff pre/post delete.

usage: summarize.py <runs-dir> <tag>
Prints one block per run: version, HTTP code + body of the DELETE, every path removed or
added between snap-pre-delete and snap-post-delete (relative to the library root), the
"Deleted audiobook file" / refusal log lines, and whether bystander book D changed.
"""
from __future__ import annotations

import json
import os
import re
import sys


def manifest(path: str) -> dict[str, str]:
    """Map relative path -> 'kind inode size sha' for one snapshot file."""
    entries: dict[str, str] = {}
    if not os.path.exists(path):
        return entries
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            kind, inode, size, _mtime, digest, rel = line.rstrip("\n").split(" ", 5)
            entries[rel] = f"{kind} {inode} {size} {digest[:12]}"
    return entries


def delete_line(run: str) -> str:
    """Return the api.log line for the DELETE request."""
    with open(os.path.join(run, "api.log"), encoding="utf-8") as handle:
        for line in handle:
            if " DELETE /library/" in line:
                return line.strip()
    return "(no DELETE issued)"


def log_lines(run: str) -> list[str]:
    """Delete-related log lines from the app console log."""
    pattern = re.compile(r"Deleted audiobook file|Refused|Blocked recursive|delete preflight|filesystem deletion was blocked", re.I)
    out = []
    with open(os.path.join(run, "out.log"), encoding="utf-8", errors="replace") as handle:
        for line in handle:
            if pattern.search(line):
                out.append(re.sub(r"/dev/shm/jj-993/runs/[^/]+/lib/", "<root>/", line.strip())[:300])
    return out


def main() -> int:
    """Print the summary."""
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    runs_dir, tag = sys.argv[1], sys.argv[2]
    for name in sorted(os.listdir(runs_dir)):
        if not name.startswith(tag + "-"):
            continue
        run = os.path.join(runs_dir, name)
        try:
            version = json.load(open(os.path.join(run, "status.json"))).get("version")
        except (OSError, ValueError):
            version = "?"
        steps = open(os.path.join(run, "steps.log"), encoding="utf-8").read() if os.path.exists(os.path.join(run, "steps.log")) else ""
        before = manifest(os.path.join(run, "snap-pre-delete.txt"))
        after = manifest(os.path.join(run, "snap-post-delete.txt"))
        removed = sorted(set(before) - set(after))
        added = sorted(set(after) - set(before))
        changed = sorted(k for k in set(before) & set(after) if before[k].split()[0] == "f" and before[k] != after[k])
        bystander = [p for p in removed + added + changed if p.startswith("Edgar Rice Burroughs")]
        print(f"=== {name}  version={version}")
        for line in steps.splitlines():
            if "outside Listenarr" in line or "no outside change" in line or "tracked inside" in line:
                print(f"  setup: {line.split(' ', 1)[1]}")
        print(f"  {re.sub(r'/dev/shm/jj-993/runs/[^/]+/lib/', '<root>/', delete_line(run))[:400]}")
        print(f"  pre-delete entries={len(before)} post-delete entries={len(after)}")
        for p in removed:
            print(f"  REMOVED {before[p].split()[0]} {p}")
        for p in added:
            print(f"  ADDED   {after[p].split()[0]} {p}")
        for p in changed:
            print(f"  CHANGED {p}")
        print(f"  bystander D changed: {'YES ' + str(bystander) if bystander else 'no'}")
        for line in log_lines(run):
            print(f"  log: {line}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
