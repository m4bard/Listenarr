#!/usr/bin/env python3
"""Print a manifest of a directory tree: type, inode, size, mtime_ns, sha256, path.

Used before/after each throwaway run; `diff` of two manifests shows anything that was
deleted, moved (inode at a new path), written (size/mtime/hash), or created.
"""
from __future__ import annotations

import hashlib
import os
import sys


def sha256(path: str) -> str:
    """Hash one regular file."""
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> int:
    """Walk argv[1] and print one line per entry, sorted by path."""
    if len(sys.argv) != 2:
        print("usage: snapshot.py <dir>", file=sys.stderr)
        return 2
    root = sys.argv[1]
    lines = []
    for current, dirs, files in os.walk(root):
        for name in dirs + files:
            path = os.path.join(current, name)
            st = os.lstat(path)
            if os.path.islink(path):
                kind, digest = "l", os.readlink(path)
            elif os.path.isdir(path):
                kind, digest = "d", "-"
            else:
                kind, digest = "f", sha256(path)
            lines.append(f"{kind} {st.st_ino} {st.st_size if kind != 'd' else 0} {st.st_mtime_ns} {digest} {os.path.relpath(path, root)}")
    lines.sort(key=lambda line: line.split(" ", 5)[5])
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main())
