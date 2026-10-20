# Measure 3: file-metadata rescan on one stale book and via the task, provider refresh on one
# and on all (providers unreachable in the namespace), then every task the API lets us run.
source "$(dirname "${BASH_SOURCE[0]}")/reads.sh"
reads_pass before
api POST /library/3/rescan-metadata > /dev/null
api POST /library/refresh-metadata '{"force":true}' > "$RUN/refresh-all.json"
api GET /system/tasks > "$RUN/tasks.json"
for t in $(python3 -I -c '
import json,sys
d=json.load(open(sys.argv[1])); d=d.get("tasks",d) if isinstance(d,dict) else d
print(" ".join(x.get("name") or x.get("taskName") for x in d))' "$RUN/tasks.json"); do
  api POST "/system/tasks/$t/run" > /dev/null
  sleep 2
done
sleep 90
reads_pass after
