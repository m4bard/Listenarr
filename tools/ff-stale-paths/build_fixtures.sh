#!/usr/bin/env bash
# Build the two stale-path fixtures with the CONTROL (.620) build, the way production's DB aged:
#   v1: files imported at the stored paths, .620 started (identities recorded), files then
#       bulk-renamed with mv (inodes kept), .620 started again  -> v1/prod-state.db
#   v2: rows point at paths that never existed; files sit only at the renamed paths,
#       .620 started once                                     -> v2/prod-state.db
# usage: build_fixtures.sh <ctl-bin-dir> <prod-like.db> <corpus.json>
unset TMOUT
set -euo pipefail
[ $# -eq 3 ] || { sed -n '2,8p' "$0"; exit 2; }
ctl=$1 base_db=$2 corpus=$3
here=$(cd "$(dirname "$0")" && pwd)
top=${FF_TOP:-/dev/shm/ff-stale}
for v in v1 v2; do [ -e "$top/$v" ] && { echo "ERROR $top/$v exists" >&2; exit 2; }; done

mkdir -p "$top/v1/ROOT-A" "$top/v2/ROOT-A"
cp "$base_db" "$top/v1/seed.db"; cp "$base_db" "$top/v2/seed.db"
python3 -I "$here/gen_fixture.py" --corpus "$corpus" --root "$top/v1/ROOT-A" --db "$top/v1/seed.db" --phase stored --layout-out "$top/v1/layout.json"
python3 -I "$here/gen_fixture.py" --corpus "$corpus" --root "$top/v2/ROOT-A" --db "$top/v2/seed.db" --phase real --layout-out "$top/v2/layout.json"
sqlite3 "$top/v1/seed.db" < "$here/seed_extras.sql"
sqlite3 "$top/v2/seed.db" < "$here/seed_extras.sql"

FF_BASE="$top/v1" "$here/run.sh" age1-ctl "$ctl" "$top/v1/seed.db" "$here/actions/age.sh" 30
cp "$top/v1/runs/age1-ctl/config/database/listenarr.db" "$top/v1/aged1.db"
python3 -I "$here/gen_fixture.py" --corpus "$corpus" --root "$top/v1/ROOT-A" --phase rename
FF_BASE="$top/v1" "$here/run.sh" age2-ctl "$ctl" "$top/v1/aged1.db" "$here/actions/age.sh" 30
cp "$top/v1/runs/age2-ctl/config/database/listenarr.db" "$top/v1/prod-state.db"

FF_BASE="$top/v2" "$here/run.sh" age1-ctl "$ctl" "$top/v2/seed.db" "$here/actions/age.sh" 30
cp "$top/v2/runs/age1-ctl/config/database/listenarr.db" "$top/v2/prod-state.db"
sha256sum "$top"/v*/prod-state.db

# Candidate input = production's DB after the plan's section-3 rename (as production will run it).
for v in v1 v2; do
  cp "$top/$v/prod-state.db" "$top/$v/prod-state-renamed.db"
  sqlite3 "$top/$v/prod-state-renamed.db" < "${FF_RENAME_SQL:?set FF_RENAME_SQL to rename-migration-history.sql}"
done
