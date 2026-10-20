# Shared steps for the JJ scenarios, all through the stock API a user's UI calls.
# Book identifiers are corpus values (listenarr-testdata corpus.json).

# setup_root: create the library root folder the way Settings > Root Folders does, and
# confirm it if the build asks for confirmation. Prints nothing; logs to api.log.
setup_root() {
  api GET /system/info > "$RUN/status.json"
  api POST /rootfolders "{\"name\":\"Library\",\"path\":\"$LIB\",\"isDefault\":true}" > "$RUN/rootfolder-create.json"
  api GET /rootfolders > "$RUN/rootfolders.json"
  local token
  token=$(python3 -I -c '
import json,sys
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
print(find(json.load(open(sys.argv[1])),"confirmationToken") or "-")' "$RUN/rootfolders.json")
  if [ "$token" != - ]; then
    api POST /rootfolders/1/confirm-current-folder "{\"expectedCurrentPath\":\"$LIB\",\"confirmationToken\":\"$token\"}" > "$RUN/rootfolder-confirm.json"
  fi
  api GET /system/ready > "$RUN/ready.json"
}

# add_book TITLE AUTHOR ASIN SERIES POSITION YEAR NARRATOR DEST -> prints the new audiobook id
# (Add New page: metadata in the body, explicit destination folder).
add_book() {
  local body
  body=$(python3 -I -c '
import json,sys
t,a,asin,series,pos,year,narr,dest=sys.argv[1:]
print(json.dumps({"metadata":{"title":t,"authors":[a],"narrators":[narr],"asin":asin,"series":series,
  "seriesNumber":pos,"publishYear":year,"language":"english"},"monitored":False,"destinationPath":dest}))' "$@")
  api POST /library/add "$body" > "$RUN/.add.json"
  python3 -I -c 'import json,sys; d=json.load(open(sys.argv[1])); print((d.get("audiobook") or {}).get("id",""))' "$RUN/.add.json"
}

# scan_book ID: the detail page's "Scan" button, then wait for the job to finish.
scan_book() {
  local id=$1 job state
  api POST "/library/$id/scan" > "$RUN/.scan.json"
  job=$(python3 -I -c 'import json,sys
d=json.load(open(sys.argv[1]))
print(d.get("jobId") or d.get("id") or "")' "$RUN/.scan.json" 2>/dev/null || true)
  for _ in $(seq 1 60); do
    [ -n "$job" ] || { sleep 3; break; }
    state=$(api GET "/library/scan/$job" | python3 -I -c 'import json,sys; print(json.load(sys.stdin).get("status",""))' 2>/dev/null || true)
    case "$state" in Completed|completed|Failed|failed|Canceled|canceled) break;; esac
    sleep 1
  done
}

# book_files ID TAG: record the tracked rows for a book.
book_files() {
  api GET "/library/$1" > "$RUN/book-$1-$2.json"
  api GET "/library/$1/files-debug" > "$RUN/files-$1-$2.json"
  api GET "/library/$1/delete-capabilities" > "$RUN/delcap-$1-$2.json"
}
