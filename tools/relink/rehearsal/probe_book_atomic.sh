#!/usr/bin/env bash
# Probe PLAN's book-atomic rule: a book with no live row is re-linked whole or not at all.
#   control  the DB as is                                    -> the book's rows are planned
#   attack   plus one stale row of that book no file matches -> every row of the book is withheld
# usage: probe_book_atomic.sh <db> <audiobook-id> <scratch-dir>
# exit: 0 control linked and attack withheld the book, 1 otherwise, 2 usage
unset TMOUT
set -euo pipefail
[ $# -eq 3 ] || { sed -n '2,6p' "$0"; exit 2; }
db=$1 book=$2 work=$3
tool=$(cd "$(dirname "$0")/.." && pwd)
[ -e "$work" ] && { echo "ERROR $work exists" >&2; exit 2; }
mkdir -p "$work"
cp "$db" "$work/control.db"; cp "$db" "$work/attack.db"
base=$(sqlite3 "$work/attack.db" "select BasePath from Audiobooks where Id=$book")
sqlite3 "$work/attack.db" "insert into AudiobookFiles (AudiobookId, Path, Size, CreatedAt) values ($book, '$(printf %s "$base" | sed "s/'/''/g")/missing-part-99.m4b', 777, '2026-10-20 00:00:00')"
count() { python3 -I -c 'import json,sys; print(sum(1 for e in json.load(open(sys.argv[1]))["relinks"] if e["audiobook_id"] == int(sys.argv[2])))' "$1" "$book"; }
for case in control attack; do
  (cd "$tool" && .venv/bin/python -m listenarr_relink plan --db "$work/$case.db" --out "$work/plan-$case") > /dev/null 2>&1
done
linked=$(count "$work/plan-control/plan.json"); withheld=$(count "$work/plan-attack/plan.json")
echo "control: book $book relinks $linked; attack: book $book relinks $withheld"
[ "$linked" -gt 0 ] && [ "$withheld" = 0 ]
