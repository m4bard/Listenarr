#!/usr/bin/env bash
# Rebuild every adversarial-review fixture (II-C, II-D, II-E builders) fresh and score PLAN on each, in the
# default mode (identity only) and with --include-shape-matches. One line per fixture and mode.
# usage: review_probes.sh <children-dir> <seed.db> <out-dir>
#   children-dir  holds II-C-probes/, II-D-probes/, II-E-probes/ (fixture builders and score_probe.py)
#   out-dir       new directory under /dev/shm
# exit: 0 the default mode produced no wrong link anywhere, 1 it did, 2 usage
unset TMOUT
set -uo pipefail
[ $# -eq 3 ] || { sed -n '2,7p' "$0"; exit 2; }
kids=$1 seed=$2 out=$3
tool=$(cd "$(dirname "$0")/.." && pwd)
scorer="$kids/II-C-probes/score_probe.py"
case "$out" in /dev/shm/*) ;; *) echo "ERROR out-dir must be under /dev/shm" >&2; exit 2;; esac
[ -e "$out" ] && { echo "ERROR $out exists" >&2; exit 2; }
mkdir -p "$out"

fixtures=()
for variant in atk ctl; do
  python3 -I "$kids/II-C-probes/build_probe.py" --seed "$seed" --top "$out/c-f1" --variant $variant > /dev/null && fixtures+=("$out/c-f1/$variant")
  python3 -I "$kids/II-C-probes/build_probe.py" --seed "$seed" --top "$out/c-f2" --variant $variant --untagged > /dev/null && fixtures+=("$out/c-f2/$variant")
  for name in A H; do
    python3 -I "$kids/II-D-probes/build_d.py" --seed "$seed" --top "$out/d" --fixture $name --variant $variant > /dev/null && fixtures+=("$out/d/$name/$variant")
  done
  for name in A B C D G H; do
    python3 -I "$kids/II-E-probes/build_e.py" --seed "$seed" --top "$out/e" --fixture $name --variant $variant > /dev/null && fixtures+=("$out/e/$name/$variant")
  done
done

wrong_default=0
for fx in "${fixtures[@]}"; do
  read -r -a maps <<< "$(python3 -I -c 'import json,sys; print(" ".join("--path-map " + m for m in json.load(open(sys.argv[1])).get("path_map", [])))' "$fx/truth.json")"
  root=$(sqlite3 -readonly "$fx/db.sqlite" "select Path from RootFolders order by Id limit 1")
  if [ ${#maps[@]} = 0 ] && [ ! -d "$root" ]; then maps=(--path-map "$root=$fx/ROOT-A"); fi  # II-D H stores /data/lib
  for mode in default shape; do
    extra=(); [ $mode = shape ] && extra=(--include-shape-matches)
    (cd "$tool" && .venv/bin/python -m listenarr_relink plan --db "$fx/db.sqlite" --out "$fx/plan-$mode" "${maps[@]}" "${extra[@]}") \
      > /dev/null 2> "$fx/plan-$mode.log"
    rc=$?
    tally=$(python3 -I "$scorer" "$fx/truth.json" "$fx/plan-$mode/plan.json" 2>/dev/null | grep -E '^WRONG=')
    [ -n "$tally" ] || tally=no-plan
    echo "${fx#"$out"/} $mode plan-rc=$rc $tally"
    if [ $mode = default ] && ! grep -q '^WRONG=0 ' <<< "$tally"; then wrong_default=1; fi
  done
done
exit $wrong_default
