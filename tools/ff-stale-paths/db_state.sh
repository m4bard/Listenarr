#!/usr/bin/env bash
# Print a diffable summary of a Listenarr DB: row count per table, then the library rows
# (books, file rows, history, downloads, deletion intents) with the columns that matter
# for stale-path behaviour.
# usage: db_state.sh <db-file>
unset TMOUT
set -euo pipefail
[ $# -eq 1 ] || { sed -n '2,5p' "$0"; exit 2; }
db=$1
q() { sqlite3 -separator ' | ' "$db" "$1" 2>/dev/null || true; }
echo "## row counts"
for t in $(sqlite3 "$db" "select name from sqlite_master where type='table' and name not like 'sqlite_%' order by name"); do
  echo "$t $(sqlite3 "$db" "select count(*) from \"$t\"")"
done
echo "## Audiobooks (Id | Title | Monitored | FilePath | FileSize | BasePath | LastSearchTime)"
q "select Id, Title, Monitored, FilePath, FileSize, BasePath, LastSearchTime from Audiobooks order by Id"
echo "## AudiobookFiles (Id | AudiobookId | Path | Size | PathIdentityState | PathIdentityReason | physical? | Source)"
q "select Id, AudiobookId, Path, Size, PathIdentityState, PathIdentityReason, PhysicalObjectIdentity is not null, Source from AudiobookFiles order by Id"
echo "## History (Id | AudiobookId | EventType | Source | Message)"
q "select Id, AudiobookId, EventType, Source, substr(Message,1,200) from History order by Id"
echo "## Downloads"
q "select * from Downloads"
echo "## AudiobookDeletionIntents"
q "select * from AudiobookDeletionIntents"
echo "## WeakStorageScanCandidates"
q "select * from WeakStorageScanCandidates"
echo "## RootFolders"
q "select Id, Path, PathIdentityState, DirectoryObjectIdentity is not null from RootFolders"
