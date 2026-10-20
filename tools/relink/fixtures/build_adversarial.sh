#!/usr/bin/env bash
# Build the two adversarial stale-path fixtures with the CONTROL (.620) build, the way
# tools/ff-stale-paths/build_fixtures.sh builds FF's v1/v2:
#   adv-v1: files created at the stored paths, .620 started (identities recorded), files then
#           renamed (os.rename, inodes kept) and the case mutations applied, .620 started again
#   adv-v2: files created only at the real paths, .620 started once
# Each variant dir gets ROOT-A, seed.db, prod-state.db, prod-state-renamed.db, layout.json,
# expected.json and check.log. Every aging run must report ready=1 and fs_changed=no.
# usage: build_adversarial.sh <ctl-bin-dir> <prod-like.db> <corpus.json>
# env:   FF_RENAME_SQL (required) rename-migration-history.sql; II_TOP (default /dev/shm/ii-relink)
unset TMOUT
set -euo pipefail
[ $# -eq 3 ] || { sed -n '2,10p' "$0"; exit 2; }
ctl=$1 base_db=$2 corpus=$3
here=$(cd "$(dirname "$0")" && pwd)
ff="$here/../../ff-stale-paths"
top=${II_TOP:-/dev/shm/ii-relink}
gen="$here/gen_adversarial.py"
for v in adv-v1 adv-v2; do [ -e "$top/$v" ] && { echo "ERROR $top/$v exists" >&2; exit 2; }; done

log() { printf '%s %s [build_adversarial] %s\n' "$(date -u +%FT%TZ)" "$1" "$2"; }

# age <variant-dir> <label> <db-in> : one .620 run; fails unless it came up and left ROOT-A untouched
age() {
  local out
  out=$(FF_BASE="$1" "$ff/run.sh" "$2" "$ctl" "$3" "$ff/actions/age.sh" 30 | tee /dev/stderr)
  grep -q 'ready=1 fs_changed=no' <<<"$out" || { log ERROR "$1 $2 did not report ready=1 fs_changed=no"; exit 1; }
}

mkdir -p "$top/adv-v1/ROOT-A" "$top/adv-v2/ROOT-A"
cp "$base_db" "$top/adv-v1/seed.db"; cp "$base_db" "$top/adv-v2/seed.db"
python3 -I "$gen" --phase stored --corpus "$corpus" --root "$top/adv-v1/ROOT-A" --db "$top/adv-v1/seed.db" \
  --layout-out "$top/adv-v1/layout.json" --expected-out "$top/adv-v1/expected.json"
python3 -I "$gen" --phase real --corpus "$corpus" --root "$top/adv-v2/ROOT-A" --db "$top/adv-v2/seed.db" \
  --layout-out "$top/adv-v2/layout.json" --expected-out "$top/adv-v2/expected.json"
sqlite3 "$top/adv-v1/seed.db" < "$ff/seed_extras.sql"
sqlite3 "$top/adv-v2/seed.db" < "$ff/seed_extras.sql"

age "$top/adv-v1" age1-ctl "$top/adv-v1/seed.db"
cp "$top/adv-v1/runs/age1-ctl/config/database/listenarr.db" "$top/adv-v1/aged1.db"
python3 -I "$gen" --phase rename --corpus "$corpus" --root "$top/adv-v1/ROOT-A"
age "$top/adv-v1" age2-ctl "$top/adv-v1/aged1.db"
cp "$top/adv-v1/runs/age2-ctl/config/database/listenarr.db" "$top/adv-v1/prod-state.db"

age "$top/adv-v2" age1-ctl "$top/adv-v2/seed.db"
cp "$top/adv-v2/runs/age1-ctl/config/database/listenarr.db" "$top/adv-v2/prod-state.db"
sha256sum "$top"/adv-v*/prod-state.db

for v in adv-v1 adv-v2; do
  cp "$top/$v/prod-state.db" "$top/$v/prod-state-renamed.db"
  sqlite3 "$top/$v/prod-state-renamed.db" < "${FF_RENAME_SQL:?set FF_RENAME_SQL to rename-migration-history.sql}"
  python3 -I "$gen" --phase check --expected "$top/$v/expected.json" 2>&1 | tee "$top/$v/check.log"
done
