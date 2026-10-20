#!/usr/bin/env python3
"""Summarize one or more throwaway runs made by run.sh, side by side.

For each run dir: whether the library tree changed (and how), table row-count deltas,
AudiobookFiles rows removed/added/changed, Audiobooks whose FilePath/BasePath/FileSize
changed, History rows added by EventType, stub requests by kind (indexer search, client
add = a grab, client poll), API status codes, and the top WRN/ERR log classes.
"""
from __future__ import annotations

import collections
import re
import sqlite3
import sys
from pathlib import Path


def section(text: str, name: str) -> list[str]:
    """Lines of one '## name' section of a db_state.sh dump."""
    out, inside = [], False
    for line in text.splitlines():
        if line.startswith("## "):
            inside = line[3:].startswith(name)
            continue
        if inside and line.strip():
            out.append(line)
    return out


def counts(text: str) -> dict[str, int]:
    """Table row counts from a db_state.sh dump."""
    result = {}
    for line in section(text, "row counts"):
        name, _, value = line.rpartition(" ")
        result[name] = int(value)
    return result


def keyed(lines: list[str]) -> dict[str, str]:
    """Map first column (Id) to the whole row."""
    return {line.split(" | ", 1)[0]: line for line in lines}


def fs_changes(run: Path) -> list[str]:
    """Describe tree differences between fs-before and fs-after."""
    def load(name: str) -> dict[str, tuple[str, ...]]:
        rows = {}
        for line in (run / name).read_text(encoding="utf-8").splitlines():
            kind, inode, size, mtime, digest, path = line.split(" ", 5)
            rows[path] = (kind, inode, size, mtime, digest)
        return rows
    before, after = load("fs-before.txt"), load("fs-after.txt")
    notes = []
    for path in sorted(before.keys() - after.keys()):
        notes.append(f"GONE {before[path][0]} {path}")
    for path in sorted(after.keys() - before.keys()):
        notes.append(f"NEW {after[path][0]} {path}")
    for path in sorted(before.keys() & after.keys()):
        b, a = before[path], after[path]
        if b[0] == "f" and (b[4] != a[4] or b[1] != a[1]):
            notes.append(f"CHANGED file {path}")
        elif b[0] == "d" and b[3] != a[3]:
            notes.append(f"MTIME dir {path}")
    return notes


def stub_kinds(run: Path) -> collections.Counter[str]:
    """Classify stub requests."""
    kinds: collections.Counter[str] = collections.Counter()
    log = run / "stub-requests.log"
    if not log.exists():
        return kinds
    for line in log.read_text(encoding="utf-8").splitlines():
        if "port=9696" in line:
            kinds["indexer:" + ("caps" if "t=caps" in line else "search" if "t=search" in line or "t=book" in line else "other")] += 1
        elif "/torrents/add" in line:
            kinds["client:ADD(grab)"] += 1
        else:
            kinds["client:poll"] += 1
    return kinds


def log_classes(run: Path) -> collections.Counter[str]:
    """Count WRN/ERR lines by source class + first words of the message."""
    classes: collections.Counter[str] = collections.Counter()
    pattern = re.compile(r"\[(WRN|ERR|FTL)\] \[([^\]]*)\] (.{0,70})")
    for line in (run / "out.log").read_text(encoding="utf-8", errors="replace").splitlines():
        match = pattern.search(line)
        if match:
            message = re.sub(r"\d+", "N", re.sub(r"'[^']*'|\"[^\"]*\"|/\S+", "X", match.group(3)))
            classes[f"{match.group(1)} {match.group(2).rsplit('.', 1)[-1]}: {message}"] += 1
    return classes


def api_codes(run: Path) -> collections.Counter[str]:
    """Count API responses by method, route shape and status."""
    codes: collections.Counter[str] = collections.Counter()
    log = run / "api.log"
    if not log.exists():
        return codes
    for line in log.read_text(encoding="utf-8").splitlines():
        parts = line.split(" ")
        if len(parts) >= 5:
            route = re.sub(r"/\d+", "/{id}", parts[2].split("?")[0])
            codes[f"{parts[1]} {route} -> {parts[4]}"] += 1
    return codes


def summarize(run: Path) -> str:
    """Render one run's summary."""
    before = (run / "db-before.txt").read_text(encoding="utf-8")
    after = (run / "db-after.txt").read_text(encoding="utf-8")
    out = [f"### {run.name}", f"ready: {(run / 'ready').read_text().strip() if (run / 'ready').exists() else '?'}"]
    fs = fs_changes(run)
    out.append(f"library tree: {'UNCHANGED' if not fs else f'{len(fs)} change(s)'}")
    out += [f"  {n}" for n in fs[:40]]
    cb, ca = counts(before), counts(after)
    deltas = {t: ca.get(t, 0) - cb.get(t, 0) for t in sorted(set(cb) | set(ca)) if ca.get(t, 0) != cb.get(t, 0)}
    out.append(f"row-count deltas: {deltas or 'none'}")
    fb = keyed(section(before, "AudiobookFiles"))
    fa = keyed(section(after, "AudiobookFiles"))
    removed = sorted(fb.keys() - fa.keys(), key=int)
    added = sorted(fa.keys() - fb.keys(), key=int)
    changed = sorted((k for k in fb.keys() & fa.keys() if fb[k] != fa[k]), key=int)
    out.append(f"AudiobookFiles: removed {len(removed)}, added {len(added)}, changed {len(changed)}")
    out += [f"  - {fb[k]}" for k in removed[:60]]
    out += [f"  + {fa[k]}" for k in added[:60]]
    out += [f"  ~ {fb[k]}\n    => {fa[k]}" for k in changed[:20]]
    bb = keyed(section(before, "Audiobooks"))
    ba = keyed(section(after, "Audiobooks"))
    gone_books = sorted(bb.keys() - ba.keys(), key=int)
    new_books = sorted(ba.keys() - bb.keys(), key=int)
    path_changed = [k for k in bb.keys() & ba.keys() if bb[k].split(" | ")[3:6] != ba[k].split(" | ")[3:6]]
    searched = [k for k in ba if ba[k].split(" | ")[-1].strip()]
    out.append(f"Audiobooks: removed {gone_books or 'none'}, new {new_books or 'none'}, FilePath/FileSize/BasePath changed {sorted(path_changed, key=int) or 'none'}, with LastSearchTime {len(searched)}")
    hb = keyed(section(before, "History"))
    ha = keyed(section(after, "History"))
    new_history = collections.Counter(ha[k].split(" | ")[2] for k in ha.keys() - hb.keys())
    out.append(f"History added: {dict(new_history) or 'none'}")
    out.append(f"stub requests: {dict(stub_kinds(run)) or 'none'}")
    out.append("api: " + "; ".join(f"{k} x{v}" for k, v in sorted(api_codes(run).items())))
    out.append("log WRN/ERR classes:")
    out += [f"  {v:5d} {k}" for k, v in log_classes(run).most_common(15)]
    return "\n".join(out)


def main() -> int:
    """Summarize each run dir on the command line."""
    if len(sys.argv) < 2:
        print("usage: summarize.py <run-dir>...", file=sys.stderr)
        return 2
    print("\n\n".join(summarize(Path(arg)) for arg in sys.argv[1:]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
