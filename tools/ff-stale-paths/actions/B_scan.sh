# Measure 2: per-book scan of every book (the only reconciling scan), then an unmatched scan of ROOT-A.
source "$(dirname "${BASH_SOURCE[0]}")/reads.sh"
reads_pass before
for id in $(python3 -I -c 'import json,sys; print(" ".join(str(b["id"]) for b in json.load(open(sys.argv[1]))))' "$RUN/library-before.json"); do
  api POST "/library/$id/scan" '{}' > "$RUN/.scan-$id.json"
  sleep 1
done
sleep 45
api POST /rootfolders/1/scan-unmatched '{}' > "$RUN/unmatched-start.json"
sleep 30
job=$(python3 -I -c 'import json,sys; d=json.load(open(sys.argv[1])); print(d.get("jobId") or d.get("id") or "")' "$RUN/unmatched-start.json" 2>/dev/null || true)
[ -n "$job" ] && api GET "/rootfolders/unmatched-results/$job" > "$RUN/unmatched-results.json"
api GET /rootfolders/1/unmatched > "$RUN/unmatched.json"
reads_pass after
