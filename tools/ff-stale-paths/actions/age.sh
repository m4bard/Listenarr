# Aging run (control build): confirm the library root the way an operator would, so the
# fixture's ROOT-A is an authorized, enrolled root like production's, then record readiness.
api GET /system/ready > "$RUN/ready-before.json"
api GET /rootfolders > "$RUN/rootfolders-before.json"
read -r root_path token < <(python3 -I -c '
import json,sys
roots=json.load(open(sys.argv[1]))
def find(o,k):
    if isinstance(o,dict):
        if o.get(k): return o[k]
        for v in o.values():
            r=find(v,k)
            if r: return r
    if isinstance(o,list):
        for v in o:
            r=find(v,k)
            if r: return r
r=roots[0]; print(r.get("path"), find(r,"confirmationToken") or "-")' "$RUN/rootfolders-before.json")
if [ "$token" != - ]; then
  api POST /rootfolders/1/confirm-current-folder "{\"expectedCurrentPath\":\"$root_path\",\"confirmationToken\":\"$token\"}" > "$RUN/confirm.json"
fi
api GET /rootfolders > "$RUN/rootfolders-after.json"
api GET /system/ready > "$RUN/ready-after.json"
