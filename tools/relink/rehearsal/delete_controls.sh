#!/usr/bin/env bash
# Paired measurement for the round 27 candidate's recursive book-folder delete, with and without the re-link.
# Every pair runs on two fixture trees built identically and independently (a delete mutates its tree):
#   result   full rehearsal (plan -> .620 own mechanisms -> apply -> .620 start) then candidate deletes the book
#   control  candidate deletes the same book on the UN-relinked DB (prod-state-renamed.db), nothing else
# Pairs:
#   adv-v1, adv-v2  C10 mixed book 22; book 23's file sits in book 22's folder under Extras/
#   ff-v2           FF mixed book 5 with book 42's file moved into a subfolder (add_foreign_file.sh)
# The result is meaningful only if its control comes out differently (the foreign file goes).
# usage: delete_controls.sh <ctl-bin> <cand-bin> <prod-like.db> <corpus.json> <top-dir>
# env:   FF_RENAME_SQL (required); RH_PLAN_ARGS passed to every PLAN of the result rehearsals
# exit:  0 every pair ran (read pairs.txt for the outcome), 1 a step failed, 2 usage
unset TMOUT
set -euo pipefail
[ $# -eq 5 ] || { sed -n '2,13p' "$0"; exit 2; }
ctl=$1 cand=$2 base_db=$3 corpus=$4 top=$5
: "${FF_RENAME_SQL:?set FF_RENAME_SQL to rename-migration-history.sql}"
here=$(cd "$(dirname "$0")" && pwd)
ff="$here/../../ff-stale-paths"
case "$top" in /dev/shm/*) ;; *) echo "ERROR top must be under /dev/shm" >&2; exit 2;; esac
[ -e "$top" ] && { echo "ERROR $top exists" >&2; exit 2; }
mkdir -p "$top"
log() { printf '%s %s [delete_controls] %s\n' "$(date -u +%FT%TZ)" "$1" "$2" | tee -a "$top/pairs.txt"; }

# Book 42's file really lives in book 5's subfolder after add_foreign_file.sh; record that as its truth.
move_foreign() {
  "$ff/add_foreign_file.sh" "$1/ROOT-A"
  python3 -I - "$1/layout.json" <<'EOF'
import json, sys
path = sys.argv[1]
layout = json.load(open(path, encoding="utf-8"))
old_tail = "/D'Artagnan: The Musketeers Cycle/The Man in the Iron Mask (2010)/The Man in the Iron Mask.m4b"
new_tail = "/The Three Musketeers/D'Artagnan/The Man in the Iron Mask.m4b"
hits = 0
for book in layout:
    for i, real in enumerate(book["real"]):
        if real.endswith(old_tail):
            book["real"][i] = real[: -len(old_tail)] + new_tail
            hits += 1
if hits != 1:
    raise SystemExit(f"ERROR expected one foreign-file entry in layout, found {hits}")
json.dump(layout, open(path, "w", encoding="utf-8"), indent=1)
EOF
}

# removed_files <run-dir>: regular files present before the run and absent after (by path)
removed_files() {
  LC_ALL=C comm -23 <(grep '^f ' "$1/fs-before.txt" | cut -d' ' -f6- | LC_ALL=C sort) \
    <(grep '^f ' "$1/fs-after.txt" | cut -d' ' -f6- | LC_ALL=C sort)
}

# report <label> <run-dir> <foreign-path-suffix>
report() {
  local removed foreign
  removed=$(removed_files "$2")
  printf '%s\n' "$removed" > "$top/$1.removed.txt"
  foreign=$(grep -cF "$3" <<<"$removed" || true)
  log INFO "$1: $(grep ' DELETE ' "$2/api.log" | cut -c1-260)"
  log INFO "$1: files removed $(grep -c . <<<"$removed" || true); foreign file removed: $([ "$foreign" -gt 0 ] && echo YES || echo no)"
}

log INFO "building fixtures under $top"
II_TOP="$top/adv-res" "$here/../fixtures/build_adversarial.sh" "$ctl" "$base_db" "$corpus" > "$top/build-adv-res.log" 2>&1 &
II_TOP="$top/adv-ctl" "$here/../fixtures/build_adversarial.sh" "$ctl" "$base_db" "$corpus" > "$top/build-adv-ctl.log" 2>&1 &
FF_TOP="$top/ff-res" "$ff/build_fixtures.sh" "$ctl" "$base_db" "$corpus" > "$top/build-ff-res.log" 2>&1 &
FF_TOP="$top/ff-ctl" "$ff/build_fixtures.sh" "$ctl" "$base_db" "$corpus" > "$top/build-ff-ctl.log" 2>&1 &
for job in $(jobs -p); do wait "$job" || { log ERROR "a fixture build failed (see build-*.log)"; exit 1; }; done
move_foreign "$top/ff-res/v2"
move_foreign "$top/ff-ctl/v2"

log INFO "running results and controls"
pids=()
for v in adv-v1 adv-v2; do
  "$here/rehearse.sh" "$top/adv-res/$v" b "$ctl" "$cand" 22 > "$top/rh-$v.log" 2>&1 & pids+=($!)
  FF_BASE="$top/adv-ctl/$v" RH_DELETE_BOOK=22 "$ff/run.sh" ctl-cand-delete "$cand" "$top/adv-ctl/$v/prod-state-renamed.db" \
    "$here/actions/cand_delete.sh" 10 > "$top/ctl-$v.log" 2>&1 & pids+=($!)
done
"$here/rehearse.sh" "$top/ff-res/v2" b "$ctl" "$cand" 5 > "$top/rh-ff-v2.log" 2>&1 & pids+=($!)
FF_BASE="$top/ff-ctl/v2" RH_DELETE_BOOK=5 "$ff/run.sh" ctl-cand-delete "$cand" "$top/ff-ctl/v2/prod-state-renamed.db" \
  "$here/actions/cand_delete.sh" 10 > "$top/ctl-ff-v2.log" 2>&1 & pids+=($!)
rc=0
for pid in "${pids[@]}"; do wait "$pid" || rc=1; done
[ $rc = 0 ] || log WARN "a rehearsal reported a failed gate (see rh-*.log and rehearsal-b/summary.txt)"

for v in adv-v1 adv-v2; do
  report "result-$v" "$top/adv-res/$v/runs/rh-b-cand-delete" "Extras/"
  report "control-$v" "$top/adv-ctl/$v/runs/ctl-cand-delete" "Extras/"
done
report result-ff-v2 "$top/ff-res/v2/runs/rh-b-cand-delete" "D'Artagnan/The Man in the Iron Mask.m4b"
report control-ff-v2 "$top/ff-ctl/v2/runs/ctl-cand-delete" "D'Artagnan/The Man in the Iron Mask.m4b"
log INFO "done"
