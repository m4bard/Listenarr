# Measure 4: delete-from-library variants on books with stale paths, plus a live book as the
# positive control that the delete path really deletes when it should.
# Book ids are fixture index + 1 (see gen_fixture.py):
#   2  fresh, resolves                         deleteFiles=true             positive control
#   4  stale multi-file, folder moved           deleteFiles=true             stored folder gone
#   5  mixed: part 1 live, part 2 renamed in the same folder   deleteFiles=true
#   6  stale, folder moved                      deleteFiles=true&deleteFolder=true
#   7  stale, renamed in same folder, EMPTY BasePath   deleteFiles=true (folder from FilePath parent)
#   8  stale, folder moved                      deleteFiles=false            DB-only
#   9  stale, renamed in same folder            deleteFiles=true             renamed file sits in BasePath
#   11 stale, renamed in same folder            deleteFiles=true&deleteFolder=true
source "$(dirname "${BASH_SOURCE[0]}")/reads.sh"
reads_pass before
for spec in "2:deleteFiles=true" "4:deleteFiles=true" "5:deleteFiles=true" "6:deleteFiles=true&deleteFolder=true" "7:deleteFiles=true" "8:deleteFiles=false" "9:deleteFiles=true" "11:deleteFiles=true&deleteFolder=true"; do
  id=${spec%%:*} q=${spec#*:}
  api GET "/library/$id/delete-capabilities" > "$RUN/delcap-$id.json"
  api DELETE "/library/$id?$q" > "$RUN/delete-$id.json"
  sleep 2
done
sleep 10
reads_pass after
