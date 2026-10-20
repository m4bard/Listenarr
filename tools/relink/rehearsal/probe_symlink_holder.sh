#!/usr/bin/env bash
# Probe APPLY's holder-by-object check in isolation: after PLAN, another book's row starts reaching the
# planned target through a symlink. A symlink elsewhere leaves the target's ctime and link count alone,
# so only the (device, inode) holder check can refuse it.
#   attack   symlink + new row on the symlink path  -> APPLY must refuse (exit 3, nothing written)
#   control  symlink only, no row                   -> APPLY dry run exit 0
# usage: probe_symlink_holder.sh <fixture-dir> <row-id> <holder-audiobook-id>
#   fixture-dir has db.sqlite and plan1/ (plan.json, plan1.sha next to it); row-id is a planned relink
# exit: 0 probe behaved as expected (attack refused, control passed), 1 otherwise, 2 usage
unset TMOUT
set -euo pipefail
[ $# -eq 3 ] || { sed -n '2,9p' "$0"; exit 2; }
fx=$1 row=$2 holder_book=$3
tool=$(cd "$(dirname "$0")/.." && pwd)
work="$fx/symlink-holder"
rm -rf "$work"; mkdir -p "$work"
target=$(python3 -I -c 'import json,sys; print(next(e["host_new_path"] for e in json.load(open(sys.argv[1]))["relinks"] if e["row_id"] == int(sys.argv[2])))' "$fx/plan1/plan.json" "$row")
link="$(dirname "$target")/.holder-alias.mp3"
sha=$(cat "$fx/plan1.sha")
ln -s "$target" "$link"
trap 'rm -f "$link"' EXIT
result=0
for case in attack control; do
  cp "$fx/db.sqlite" "$work/db-$case.sqlite"
  sqlite3 "$work/db-$case.sqlite" ".backup '$work/backup-$case.sqlite'"
  if [ "$case" = attack ]; then
    sqlite3 "$work/db-$case.sqlite" "insert into AudiobookFiles (AudiobookId, Path, Size, CreatedAt) values ($holder_book, '$(printf %s "$link" | sed "s/'/''/g")', $(stat -c %s "$target"), '2026-10-20 00:00:00')"
  fi
  (cd "$tool" && .venv/bin/python -m listenarr_relink apply --db "$work/db-$case.sqlite" --plan "$fx/plan1/plan.json" \
    --plan-sha256 "$sha" --backup "$work/backup-$case.sqlite" --confirm-service-stopped --dry-run --out "$work/out-$case") \
    > /dev/null 2> "$work/$case.log" && rc=0 || rc=$?
  echo "[$case] apply --dry-run rc=$rc $(grep -o "reached by row.*" "$work/$case.log" | head -1)"
  if [ "$case" = attack ] && [ $rc != 3 ]; then result=1; fi
  if [ "$case" = control ] && [ $rc != 0 ]; then result=1; fi
done
exit $result
