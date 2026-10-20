# Delete-with-files scenarios on a stock instance. Variant comes from $JJ_VARIANT:
#   S1   mixed: book A part 2 renamed outside Listenarr, and book B's folder moved into A's
#        folder outside Listenarr (one reorganisation in a file manager). Delete A, files only.
#   S1F  as S1, but the dialog's "also remove the folder" option too.
#   C1   control: A untouched (all tracked files live, nothing nested). Delete A, files only.
#   C2   control: A untouched, B's folder moved into A's folder. Delete A, files only.
#   C3   control: both A parts renamed outside Listenarr (no live tracked file). Delete A.
#   C4   control: B added with its folder inside A's folder and scanned (B tracked there). Delete A.
# Every variant also has bystander book D in its own folder, which must never change.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
variant=${JJ_VARIANT:?set JJ_VARIANT}
note "variant=$variant"
setup_root

a_dir="$LIB/Alexandre Dumas/The Three Musketeers"
b_dir="$LIB/Alexandre Dumas/The Man in the Iron Mask"
d_dir="$LIB/Edgar Rice Burroughs/A Princess of Mars"
[ "$variant" = C4 ] && b_dir="$a_dir/The Man in the Iron Mask"

a=$(add_book "The Three Musketeers" "Alexandre Dumas" B002UZJF4U "D'Artagnan: The Musketeers Cycle" 1 2009 "John Lee" "$a_dir")
b=$(add_book "The Man in the Iron Mask" "Alexandre Dumas" B0038G2TFW "D'Artagnan: The Musketeers Cycle" 6 2010 "Simon Vance" "$b_dir")
d=$(add_book "A Princess of Mars" "Edgar Rice Burroughs" B008DFUGCQ "Barsoom" 1 2012 "Bob Thomley" "$d_dir")
note "ids a=$a b=$b d=$d"
mkaudio "$a_dir/The Three Musketeers - Part 1.m4b" "The Three Musketeers" "Alexandre Dumas" 1
mkaudio "$a_dir/The Three Musketeers - Part 2.m4b" "The Three Musketeers" "Alexandre Dumas" 2
mkaudio "$b_dir/The Man in the Iron Mask.m4b" "The Man in the Iron Mask" "Alexandre Dumas" 1
mkaudio "$d_dir/A Princess of Mars.m4b" "A Princess of Mars" "Edgar Rice Burroughs" 1
# B first, so in C4 B owns its nested file before A is scanned.
for id in $b $a $d; do scan_book "$id"; book_files "$id" imported; done
snap imported

case "$variant" in
  S1|S1F)
    mv "$a_dir/The Three Musketeers - Part 2.m4b" "$a_dir/The Three Musketeers (Part 2 of 2).m4b"
    mv "$b_dir" "$a_dir/The Man in the Iron Mask"
    note "outside Listenarr: renamed A part 2; moved B's folder into A's folder" ;;
  C1) note "no outside change" ;;
  C2)
    mv "$b_dir" "$a_dir/The Man in the Iron Mask"
    note "outside Listenarr: moved B's folder into A's folder" ;;
  C3)
    mv "$a_dir/The Three Musketeers - Part 1.m4b" "$a_dir/The Three Musketeers (Part 1 of 2).m4b"
    mv "$a_dir/The Three Musketeers - Part 2.m4b" "$a_dir/The Three Musketeers (Part 2 of 2).m4b"
    note "outside Listenarr: renamed both A parts" ;;
  C4) note "B tracked inside A's folder; no outside change" ;;
  *) note "unknown variant"; return 0 ;;
esac

for id in $a $b $d; do book_files "$id" pre-delete; done
snap pre-delete
query="deleteFiles=true"
[ "$variant" = S1F ] && query="deleteFiles=true&deleteFolder=true"
note "DELETE /library/$a?$query"
api DELETE "/library/$a?$query" > "$RUN/delete-response.json"
sleep 3
api GET /library > "$RUN/library-after.json"
for id in $b $d; do book_files "$id" post-delete; done
snap post-delete
