#!/usr/bin/env bash
# Adversarial variant for the delete test: move another book's real (renamed) file into a
# subfolder of book 5's folder (the mixed book), as a bulk rename into {Author}/{Series}/...
# could when a series folder shares a book folder's name. The other book's DB row stays stale.
# usage: add_foreign_file.sh <ROOT-A>
unset TMOUT
set -euo pipefail
[ $# -eq 1 ] || { sed -n '2,5p' "$0"; exit 2; }
root=$1
src="$root/Alexandre Dumas/D'Artagnan: The Musketeers Cycle/The Man in the Iron Mask (2010)/The Man in the Iron Mask.m4b"
dst_dir="$root/Alexandre Dumas/The Three Musketeers/D'Artagnan"
[ -f "$src" ] || { echo "ERROR missing $src" >&2; exit 1; }
mkdir -p "$dst_dir"
mv "$src" "$dst_dir/The Man in the Iron Mask.m4b"
echo "moved foreign file into $dst_dir"
