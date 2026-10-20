#!/usr/bin/env bash
# Run every delete scenario against both builds (bare 1.3.14 and 1.3.4, the release before
# #993), in parallel: each run has its own namespace, content root and library root.
# usage: run_all.sh <bin-1314> <bin-134> [tag]
# exit: 0 all runs became ready, 1 at least one did not, 2 usage
unset TMOUT
set -euo pipefail
[ $# -ge 2 ] || { sed -n '2,5p' "$0"; exit 2; }
bin1314=$1 bin134=$2 tag=${3:-r1}
here=$(cd "$(dirname "$0")" && pwd)
pids=()
for variant in S1 S1F C1 C2 C3 C4; do
  for side in 1314 134; do
    bin=$bin1314; [ $side = 134 ] && bin=$bin134
    JJ_VARIANT=$variant "$here/run_scenario.sh" "$tag-$variant-$side" "$bin" "$here/scenarios/delete.sh" &
    pids+=($!)
  done
done
rc=0
for pid in "${pids[@]}"; do wait "$pid" || rc=1; done
exit $rc
