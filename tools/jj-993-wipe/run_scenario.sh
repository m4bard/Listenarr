#!/usr/bin/env bash
# Start a built Listenarr API from a FRESH content root (no DB: stock migrations create it)
# inside a network namespace with loopback only, then source a scenario script that drives
# the stock API and the filesystem like a user would. Throwaway: everything lives under
# $JJ_BASE/runs/<label> on tmpfs.
#
# usage: run_scenario.sh <label> <bin-dir> <scenario.sh>
#   label        run name; the run dir must not exist
#   bin-dir      `dotnet build listenarr.api/Listenarr.Api.csproj -c Release -o <bin-dir>` output
#   scenario.sh  bash fragment sourced once the API answers. It gets:
#                  BASE KEY RUN LIB (the library root directory, created empty)
#                  api METHOD PATH [json]   -> body on stdout, one line per call in $RUN/api.log
#                  snap TAG                 -> full-tree manifest of $LIB to $RUN/snap-TAG.txt
#                  note TEXT                -> timestamped line in $RUN/steps.log
#                  mkaudio PATH TITLE AUTHOR PART  -> short tagged audio file
# env: JJ_BASE (default /dev/shm/jj-993), JJ_PORT (default 5993)
# exit: 0 scenario ran, 1 API never answered, 2 usage
unset TMOUT
set -euo pipefail
[ $# -eq 3 ] || { sed -n '2,18p' "$0"; exit 2; }
label=$1 bin=$2 scenario=$3
here=$(cd "$(dirname "$0")" && pwd)
scenario=$(cd "$(dirname "$scenario")" && pwd)/$(basename "$scenario")
base_dir=${JJ_BASE:-/dev/shm/jj-993}
run="$base_dir/runs/$label"
port=${JJ_PORT:-5993}
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"

log() { printf '%s %s [run_scenario %s] %s\n' "$(date -u +%FT%TZ)" "$1" "$label" "$2"; }
case "$base_dir" in /dev/shm/*) ;; *) log ERROR "JJ_BASE must be under /dev/shm"; exit 2;; esac
[ -e "$run" ] && { log ERROR "run dir $run exists"; exit 2; }
[ -f "$bin/Listenarr.Api.dll" ] || { log ERROR "no Listenarr.Api.dll in $bin"; exit 2; }

mkdir -p "$run/config" "$run/lib"
cat > "$run/config/config.json" <<'EOF'
{"LogLevel": "Information", "Port": 5000, "UrlBase": "/", "BindAddress": "127.0.0.1",
 "ApiKey": "jj993throwawaykey0000000000000000000000000", "AuthenticationRequired": "false",
 "LaunchBrowser": false, "AnalyticsEnabled": false, "UpdateMechanism": "BuiltIn", "Branch": "main",
 "InstanceName": "Listenarr", "ApiVersion": "1", "EnableSsl": false, "SslPort": 6868}
EOF
log INFO "start bin=$bin scenario=$scenario"

cat > "$run/inner.sh" <<EOF
set -uo pipefail
ip link set lo up
cd "$run"
python3 -I "$here/../ff-stale-paths/stub_server.py" "$run/stub-requests.log" 9696 8081 &
stub=\$!
( cd "$bin" && LISTENARR_CONTENT_ROOT="$run" ASPNETCORE_URLS="http://127.0.0.1:$port" exec dotnet Listenarr.Api.dll ) > "$run/out.log" 2>&1 &
app=\$!
BASE="http://127.0.0.1:$port/api/v1" KEY="jj993throwawaykey0000000000000000000000000" RUN="$run" LIB="$run/lib"
api() {
  local method=\$1 path=\$2 body=\${3:-}
  local out; out=\$(curl -sS -m 120 -o "\$RUN/.api-body" -w '%{http_code}' -X "\$method" -H "X-Api-Key: \$KEY" \
    -H 'Content-Type: application/json' \${body:+--data "\$body"} "\$BASE\$path")
  printf '%s %s %s -> %s %s\n' "\$(date -u +%T)" "\$method" "\$path" "\$out" "\$(head -c 600 "\$RUN/.api-body" | tr '\n' ' ')" >> "\$RUN/api.log"
  cat "\$RUN/.api-body"
}
snap() { python3 -I "$here/../ff-stale-paths/snapshot.py" "\$LIB" > "\$RUN/snap-\$1.txt"; }
note() { printf '%s %s\n' "\$(date -u +%T)" "\$*" >> "\$RUN/steps.log"; }
mkaudio() { # mkaudio PATH TITLE AUTHOR PART
  mkdir -p "\$(dirname "\$1")"
  ffmpeg -nostdin -loglevel error -y -f lavfi -i "sine=frequency=\$((300 + \$4 * 40)):duration=2" \
    -metadata "title=\$2" -metadata "artist=\$3" -metadata "album=\$2" -metadata "album_artist=\$3" \
    -metadata "track=\$4" -c:a aac -b:a 32k -f mp4 "\$1"
}
ready=0
for _ in \$(seq 1 120); do
  if curl -s -m 2 -o /dev/null -w '%{http_code}' -H "X-Api-Key: \$KEY" "\$BASE/library" | grep -q 200; then ready=1; break; fi
  kill -0 \$app 2>/dev/null || break
  sleep 1
done
echo "ready=\$ready" > "$run/ready"
if [ \$ready = 1 ]; then source "$scenario"; sleep 3; fi
kill -TERM \$app 2>/dev/null; wait \$app 2>/dev/null
kill \$stub 2>/dev/null; wait \$stub 2>/dev/null
exit 0
EOF
unshare -rn bash "$run/inner.sh" > "$run/inner.log" 2>&1 || true
sqlite3 "$run/config/database/listenarr.db" "pragma wal_checkpoint(TRUNCATE)" >/dev/null 2>&1 || true
log INFO "done ready=$(cat "$run/ready" 2>/dev/null) api_calls=$(wc -l < "$run/api.log" 2>/dev/null || echo 0)"
grep -q 'ready=1' "$run/ready"
