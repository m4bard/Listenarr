#!/usr/bin/env bash
# Rehearse the re-link on a throwaway fixture (FF v1/v2 or adversarial), end to end:
#   plan1 -> .620 own mechanisms on the safe books (metadata-only move, per-book scan)
#   -> plan2 on the result -> apply (dry run, real, then a re-apply that must change nothing)
#   -> .620 start (identity reconciliation) -> verify, end-state score, plan3 (must be 0 relinks)
#   -> optional: round 27 candidate deletes one book with files (tree diff recorded by run.sh)
# usage: rehearse.sh <fixture-dir> <tag> <ctl-bin> [<cand-bin> <delete-book-id>]
#   fixture-dir  has ROOT-A/, prod-state.db, and expected.json (adversarial) or layout.json (FF)
#   tag          names this rehearsal; output goes to <fixture-dir>/rehearsal-<tag> (must not exist)
#   RH_MODE=tool-only skips the own-mechanisms stage (everything through the tool)
#   RH_PLAN_ARGS extra arguments for every PLAN (e.g. --include-shape-matches)
# exit: 0 rehearsal ran and every gate passed, 1 a gate failed (see summary.txt), 2 usage
unset TMOUT
set -euo pipefail
[ $# -eq 3 ] || [ $# -eq 5 ] || { sed -n '2,11p' "$0"; exit 2; }
fx=$(cd "$1" && pwd) tag=$2 ctl=$3 cand=${4:-} del_book=${5:-}
here=$(cd "$(dirname "$0")" && pwd)
tool=$(cd "$here/.." && pwd)
ff="$tool/../ff-stale-paths"
py="$tool/.venv/bin/python"
out="$fx/rehearsal-$tag"
rename_sql=${FF_RENAME_SQL:?set FF_RENAME_SQL to rename-migration-history.sql}
[ -e "$out" ] && { echo "ERROR $out exists" >&2; exit 2; }
truth="$fx/expected.json"; [ -f "$truth" ] || truth="$fx/layout.json"
mkdir -p "$out"
export FF_BASE=$fx
fail=0
log() { printf '%s %s [rehearse %s] %s\n' "$(date -u +%FT%TZ)" "$1" "$tag" "$2" | tee -a "$out/summary.txt"; }
gate() { if eval "$2"; then log INFO "PASS $1"; else log ERROR "FAIL $1"; fail=1; fi; }
relink() { (cd "$tool" && "$py" -m listenarr_relink "$@"); }
plan_args=(${RH_PLAN_ARGS:-})
db_of() { echo "$fx/runs/$1/config/database/listenarr.db"; }

cp "$fx/prod-state.db" "$out/start.db"
relink plan --db "$out/start.db" --out "$out/plan1" "${plan_args[@]}" > "$out/plan1.sha" 2> "$out/plan1.log"
log INFO "plan1 $(sed -n 3,4p "$out/plan1/plan-summary.txt" | tr '\n' ' ')"

if [ "${RH_MODE:-}" = tool-only ]; then
  cp "$out/start.db" "$out/after-own.db"
else
  RH_PLAN="$out/plan1" "$ff/run.sh" "rh-$tag-own" "$ctl" "$out/start.db" "$here/actions/own_mechanisms.sh" 10
  cp "$(db_of "rh-$tag-own")" "$out/after-own.db"
  gate "own mechanisms left the tree unchanged" "[ ! -s '$fx/runs/rh-$tag-own/fs-diff.txt' ]"
  relink verify --db "$out/after-own.db" --plan "$out/plan1/plan.json" --out "$out/verify-own.json" > /dev/null
  log INFO "own mechanisms vs plan1: $("$py" -I -c 'import json,sys; print(json.load(open(sys.argv[1]))["plan_check"]["outcome"])' "$out/verify-own.json")"
  log INFO "api: $(grep -cE ' -> 2[0-9][0-9] ' "$fx/runs/rh-$tag-own/api.log") x2xx, $(grep -vcE ' -> 2[0-9][0-9] ' "$fx/runs/rh-$tag-own/api.log" || true) other"
  gate "own-mechanism API calls all 2xx (scan answers 202 enqueued)" "! grep -vE ' -> 2[0-9][0-9] ' '$fx/runs/rh-$tag-own/api.log' | grep -q ."
fi

relink plan --db "$out/after-own.db" --out "$out/plan2" "${plan_args[@]}" > "$out/plan2.sha" 2> "$out/plan2.log"
log INFO "plan2 $(sed -n 3,6p "$out/plan2/plan-summary.txt" | tr '\n' ' ')"
"$py" -I "$here/score.py" "$out/plan2/plan.json" "$truth" "$out/after-own.db" > "$out/score-plan2.txt" || true
log INFO "plan2 score $(head -1 "$out/score-plan2.txt")"

cp "$out/after-own.db" "$out/live.db"
sqlite3 "$out/live.db" ".backup '$out/backup.db'"
sha=$(cat "$out/plan2.sha")
cp "$out/live.db" "$out/pre-dry.db"  # a .backup copy never byte-equals its source (header change counter)
relink apply --db "$out/live.db" --plan "$out/plan2/plan.json" --plan-sha256 "$sha" --backup "$out/backup.db" \
  --confirm-service-stopped --dry-run 2> "$out/apply-dry.log" && rc=0 || rc=$?
gate "dry run exit 0 and DB byte-identical to before it" "[ $rc = 0 ] && cmp -s '$out/live.db' '$out/pre-dry.db'"
relink apply --db "$out/live.db" --plan "$out/plan2/plan.json" --plan-sha256 "$sha" --backup "$out/backup.db" \
  --confirm-service-stopped 2> "$out/apply.log" && rc=0 || rc=$?
gate "apply exit 0 and DB changed" "[ $rc = 0 ] && ! cmp -s '$out/live.db' '$out/pre-dry.db'"
cp "$out/live.db" "$out/applied.db"
relink apply --db "$out/live.db" --plan "$out/plan2/plan.json" --plan-sha256 "$sha" --backup "$out/backup.db" \
  --confirm-service-stopped --out "$out/reapply" 2> "$out/reapply.log" && rc=0 || rc=$?
relinks=$("$py" -I -c 'import json,sys; print(len(json.load(open(sys.argv[1]))["relinks"]))' "$out/plan2/plan.json")
if [ "$relinks" -gt 0 ]; then
  gate "re-apply refuses every entry (exit 3) and writes nothing" "[ $rc = 3 ] && cmp -s '$out/live.db' '$out/applied.db'"
else
  gate "re-apply of an empty plan exits 0 and writes nothing" "[ $rc = 0 ] && cmp -s '$out/live.db' '$out/applied.db'"
fi

"$ff/run.sh" "rh-$tag-start" "$ctl" "$out/applied.db" "$here/actions/start_reads.sh" 10
cp "$(db_of "rh-$tag-start")" "$out/started.db"
olog="$fx/runs/rh-$tag-start/out.log"
gate ".620 ready" "grep -q 'ready=1' '$fx/runs/rh-$tag-start/ready'"
gate ".620 start left the tree unchanged" "[ ! -s '$fx/runs/rh-$tag-start/fs-diff.txt' ]"
log INFO ".620 $(grep -o 'Reconciled [0-9]* audiobook file path identities.*' "$olog" | head -1)"
extra=$(grep -E '\[(WRN|ERR|FTL)\]' "$olog" | grep -vE 'Authentication is DISABLED|No XML encryptor|WebRootPath was not found' || true)
gate ".620 start logged no WRN/ERR beyond boilerplate" "[ -z \"\$extra\" ]"
[ -n "$extra" ] && printf '%s\n' "$extra" > "$out/start-extra-warnings.txt"
gate "every library API read returned 200" "! grep -v ' -> 200 ' '$fx/runs/rh-$tag-start/api.log' | grep -q ."
relink verify --db "$out/started.db" --plan "$out/plan2/plan.json" --out "$out/verify-started.json" > /dev/null
log INFO "verify: $("$py" -I -c 'import json,sys; r=json.load(open(sys.argv[1])); print(r["status"], r["path_identity_state"], "shared-path", len(r["rows_sharing_a_path"]), "shared-object", len(r["rows_sharing_a_file_object"]), r["plan_check"]["outcome"])' "$out/verify-started.json")"
"$py" -I "$here/score_db.py" "$out/started.db" "$truth" > "$out/score-end.txt" && rc=0 || rc=$?
log INFO "end-state score $(head -1 "$out/score-end.txt")"
gate "end state has no wrong rows" "[ $rc = 0 ]"
relink plan --db "$out/started.db" --out "$out/plan3" "${plan_args[@]}" > /dev/null 2> "$out/plan3.log"
n3=$("$py" -I -c 'import json,sys; print(len(json.load(open(sys.argv[1]))["relinks"]))' "$out/plan3/plan.json")
gate "plan3 (idempotence) proposes 0 relinks" "[ $n3 = 0 ]"

if [ -n "$cand" ]; then
  cp "$out/started.db" "$out/cand-input.db"
  sqlite3 "$out/cand-input.db" < "$rename_sql" > /dev/null
  RH_DELETE_BOOK=$del_book "$ff/run.sh" "rh-$tag-cand-delete" "$cand" "$out/cand-input.db" "$here/actions/cand_delete.sh" 10
  log INFO "candidate delete of book $del_book: $(grep ' DELETE ' "$fx/runs/rh-$tag-cand-delete/api.log" | cut -c1-300)"
  rh_removed=$(LC_ALL=C comm -23 <(grep '^< f ' "$fx/runs/rh-$tag-cand-delete/fs-diff.txt" | cut -d' ' -f7- | LC_ALL=C sort) \
    <(grep '^> f ' "$fx/runs/rh-$tag-cand-delete/fs-diff.txt" | cut -d' ' -f7- | LC_ALL=C sort) || true)
  printf '%s\n' "$rh_removed" > "$out/cand-delete-removed.txt"
  log INFO "tree diff: $(grep -c . "$out/cand-delete-removed.txt" || true) files gone (paths in cand-delete-removed.txt)"
fi
log INFO "done fail=$fail"
exit $fail
