# Round 27 candidate: how it sees the mixed book, then delete it with files. Env: RH_DELETE_BOOK.
source "$(dirname "${BASH_SOURCE[0]}")/../../../ff-stale-paths/actions/reads.sh"
reads_pass before
id=$RH_DELETE_BOOK
api GET "/library/$id/files-debug" > "$RUN/files-$id-before.json"
api GET "/library/$id/delete-capabilities" > "$RUN/delcap-$id.json"
api DELETE "/library/$id?deleteFiles=true" > "$RUN/delete-$id.json"
sleep 10
reads_pass after
