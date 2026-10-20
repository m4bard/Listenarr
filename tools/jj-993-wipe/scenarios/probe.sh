# Probe: can a stock instance be driven end to end (root, add, files, scan) offline?
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
setup_root
dest="$LIB/Alexandre Dumas/The Three Musketeers"
id=$(add_book "The Three Musketeers" "Alexandre Dumas" B002UZJF4U "D'Artagnan: The Musketeers Cycle" 1 2009 "John Lee" "$dest")
note "added id=$id"
mkaudio "$dest/The Three Musketeers - Part 1.m4b" "The Three Musketeers" "Alexandre Dumas" 1
mkaudio "$dest/The Three Musketeers - Part 2.m4b" "The Three Musketeers" "Alexandre Dumas" 2
book_files "$id" pre-scan
scan_book "$id"
book_files "$id" post-scan
snap post-scan
