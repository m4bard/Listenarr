#!/usr/bin/env bash
# Run every measurement for one fixture, control then candidate per step, serially (the
# library root is shared, so a write by one run must not be attributed to the other).
# Order: A (start + timers), C (metadata/tasks), B (scans), D (deletes) - D last because a
# delete may legitimately change the tree. Stops the stream if a non-D step changed the tree.
# usage: stream.sh <fixture-dir> <ctl-bin> <cand-bin>
unset TMOUT
set -euo pipefail
[ $# -eq 3 ] || { sed -n '2,7p' "$0"; exit 2; }
fx=$1 ctl=$2 cand=$3
here=$(cd "$(dirname "$0")" && pwd)
export FF_BASE=$fx
for step in A_start C_meta B_scan D_delete; do
  for side in ctl cand; do
    if [ $side = ctl ]; then bin=$ctl db=$fx/prod-state.db; else bin=$cand db=$fx/prod-state-renamed.db; fi
    "$here/run.sh" "$step-$side" "$bin" "$db" "$here/actions/$step.sh" 20 || echo "WARN $step-$side not ready"
    if [ "$step" != D_delete ] && [ -s "$fx/runs/$step-$side/fs-diff.txt" ]; then
      echo "ERROR $step-$side changed the library tree; later steps would not be comparable" >&2; exit 3
    fi
  done
done
