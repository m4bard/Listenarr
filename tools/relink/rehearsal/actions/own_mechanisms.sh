# .620 own mechanisms for the books PLAN marked safe: metadata-only move (moved folder, names kept),
# then per-book scan (renamed inside an existing BasePath). Env: RH_PLAN (plan dir).
api GET /system/ready > "$RUN/ready-before.json"
while IFS=$'\t' read -r id dest; do
  [ -n "$id" ] || continue
  body=$(python3 -I -c 'import json,sys; print(json.dumps({"destinationPath": sys.argv[1], "moveFiles": False}))' "$dest")
  api POST "/library/$id/move" "$body" > "$RUN/.move-$id.json"
done < "$RH_PLAN/move-books.tsv"
while read -r id; do
  [ -n "$id" ] || continue
  api POST "/library/$id/scan" '{}' > "$RUN/.scan-$id.json"
  sleep 1
done < "$RH_PLAN/scan-books.txt"
sleep 30
api GET /system/ready > "$RUN/ready-after.json"
