#!/usr/bin/env bash
# Start a built Listenarr API on a COPY of a DB, in a network namespace with loopback only,
# against the shared scratch library root, run an actions file, then stop and record
# DB and filesystem state before/after.
#
# usage: run.sh <label> <bin-dir> <db-file> <actions-file|-> [hold-seconds] [config-overlay.json]
#   label           run name; the run dir is $FF_BASE/runs/<label> and must not exist
#   bin-dir         `dotnet build listenarr.api/Listenarr.Api.csproj -c Release -o <bin-dir>` output
#   actions-file    bash fragment run inside the namespace once the API answers; it gets
#                   BASE (http://127.0.0.1:PORT/api/v1), KEY, RUN and the helper `api`
#   hold-seconds    keep the API up this long after the actions (default 20)
#   overlay         JSON merged into config/config.json before start (optional)
# env: FF_BASE (default /dev/shm/ff-stale), FF_PORT (default 5991)
# exit: 0 run completed, 1 API never answered, 2 usage
unset TMOUT
set -euo pipefail
[ $# -ge 4 ] || { sed -n '2,15p' "$0"; exit 2; }
label=$1 bin=$2 db=$3 actions=$4 hold=${5:-20} overlay=${6:-}
here=$(cd "$(dirname "$0")" && pwd)
base_dir=${FF_BASE:-/dev/shm/ff-stale}
root="$base_dir/ROOT-A"
run="$base_dir/runs/$label"
port=${FF_PORT:-5991}
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"

log() { printf '%s %s [run.sh %s] %s\n' "$(date -u +%FT%TZ)" "$1" "$label" "$2"; }
[ -e "$run" ] && { log ERROR "run dir $run exists"; exit 2; }
case "$base_dir" in /dev/shm/*) ;; *) log ERROR "FF_BASE must be under /dev/shm"; exit 2;; esac
[ -d "$root" ] || { log ERROR "no library root at $root"; exit 2; }

mkdir -p "$run/config/database"
cp "$db" "$run/config/database/listenarr.db"
python3 -I - "$run/config/config.json" "${overlay:-}" <<'EOF'
import json, sys
cfg = {"LogLevel": "Information", "Port": 5000, "UrlBase": "/", "BindAddress": "127.0.0.1",
       "ApiKey": "ffstalepathsthrowawaykey000000000000000000", "AuthenticationRequired": "false",
       "LaunchBrowser": False, "AnalyticsEnabled": False, "UpdateMechanism": "BuiltIn", "Branch": "main",
       "InstanceName": "Listenarr", "ApiVersion": "1", "EnableSsl": False, "SslPort": 6868}
if len(sys.argv) > 2 and sys.argv[2]:
    cfg.update(json.load(open(sys.argv[2])))
json.dump(cfg, open(sys.argv[1], "w"), indent=2)
EOF

"$here/db_state.sh" "$run/config/database/listenarr.db" > "$run/db-before.txt"
python3 -I "$here/snapshot.py" "$root" > "$run/fs-before.txt"
log INFO "start bin=$bin db=$(sha256sum "$db" | cut -c1-12) actions=$actions hold=$hold"

cat > "$run/inner.sh" <<EOF
set -uo pipefail
ip link set lo up
cd "$run"
python3 -I "$here/stub_server.py" "$run/stub-requests.log" 9696 8081 &
stub=\$!
( cd "$bin" && LISTENARR_CONTENT_ROOT="$run" ASPNETCORE_URLS="http://127.0.0.1:$port" exec dotnet Listenarr.Api.dll ) > "$run/out.log" 2>&1 &
app=\$!
BASE="http://127.0.0.1:$port/api/v1" KEY="ffstalepathsthrowawaykey000000000000000000" RUN="$run"
api() { # api METHOD PATH [json-body] -> prints "HTTP <code>" then body to \$RUN/api.log, body to stdout
  local method=\$1 path=\$2 body=\${3:-}
  local out; out=\$(curl -sS -m 120 -o "\$RUN/.api-body" -w '%{http_code}' -X "\$method" -H "X-Api-Key: \$KEY" \
    -H 'Content-Type: application/json' \${body:+--data "\$body"} "\$BASE\$path")
  printf '%s %s %s -> %s %s\n' "\$(date -u +%T)" "\$method" "\$path" "\$out" "\$(head -c 400 "\$RUN/.api-body" | tr '\n' ' ')" >> "\$RUN/api.log"
  cat "\$RUN/.api-body"
}
ready=0
for _ in \$(seq 1 120); do
  if curl -s -m 2 -o /dev/null -w '%{http_code}' -H "X-Api-Key: \$KEY" "\$BASE/library" | grep -q 200; then ready=1; break; fi
  kill -0 \$app 2>/dev/null || break
  sleep 1
done
echo "ready=\$ready" > "$run/ready"
if [ \$ready = 1 ] && [ "$actions" != - ]; then source "$actions"; fi
[ \$ready = 1 ] && sleep "$hold"
kill -TERM \$app 2>/dev/null; wait \$app 2>/dev/null
kill \$stub 2>/dev/null; wait \$stub 2>/dev/null
exit 0
EOF
unshare -rn bash "$run/inner.sh" > "$run/inner.log" 2>&1 || true

sqlite3 "$run/config/database/listenarr.db" "pragma wal_checkpoint(TRUNCATE)" >/dev/null 2>&1 || true
"$here/db_state.sh" "$run/config/database/listenarr.db" > "$run/db-after.txt"
python3 -I "$here/snapshot.py" "$root" > "$run/fs-after.txt"
diff "$run/fs-before.txt" "$run/fs-after.txt" > "$run/fs-diff.txt" && fsd=0 || fsd=$?
diff "$run/db-before.txt" "$run/db-after.txt" > "$run/db-diff.txt" || true
touch "$run/stub-requests.log"
log INFO "done ready=$(cat "$run/ready" 2>/dev/null) fs_changed=$([ $fsd = 0 ] && echo no || echo YES) stub_requests=$(wc -l < "$run/stub-requests.log") db_diff_lines=$(wc -l < "$run/db-diff.txt")"
grep -q 'ready=1' "$run/ready" || exit 1
