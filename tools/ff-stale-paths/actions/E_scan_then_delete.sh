# Control experiment for the mixed-book delete: rescan book 5 first (its stale row is replaced
# by the renamed file, so every tracked file is live), then delete it with files. Shows whether
# the recursive folder delete taking untracked content is pre-existing behaviour.
api GET /library/5/files-debug > "$RUN/files5-before.json"
api POST /library/5/scan '{}' > /dev/null
sleep 15
api GET /library/5/files-debug > "$RUN/files5-after-scan.json"
api DELETE "/library/5?deleteFiles=true" > "$RUN/delete-5.json"
sleep 5
