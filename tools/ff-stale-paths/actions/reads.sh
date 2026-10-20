# Measure 5: list, every detail, files-debug and delete-capabilities for every book; status codes go to api.log.
reads_pass() { # reads_pass <tag>
  local tag=$1
  api GET /system/ready > "$RUN/ready-$tag.json"
  api GET /library > "$RUN/library-$tag.json"
  for id in $(python3 -I -c 'import json,sys; print(" ".join(str(b["id"]) for b in json.load(open(sys.argv[1]))))' "$RUN/library-$tag.json"); do
    api GET "/library/$id" > /dev/null
    api GET "/library/$id/files-debug" > /dev/null
    api GET "/library/$id/delete-capabilities" > /dev/null
  done
}
