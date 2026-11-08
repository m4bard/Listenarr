"""Quarantine restore: which quarantined files may go back under a root, and the move that puts them there.

A stale row whose object (inode + file handle, on the library root's dataset) sits under an operator-named
``--restore-from`` folder is not linked there: a path outside every root is outside what a scan, a move, a
delete preflight and Audiobookshelf treat as the library. Instead the file is moved back, by a rename on the
same dataset (``link`` then ``unlink``: never over an existing name, and the object, so its identity, stays),
into the book's own folder under the root. The ordinary PLAN/APPLY then re-links the row by physical identity.

A book is restorable only when every check below passes; the first that fails names its class:

* ``not-every-row-found``: some stale row's object is neither exactly one file under a --restore-from folder on
  the root's dataset nor a file PLAN would match under the root (a book is restored whole or not at all, as
  PLAN re-links it whole);
* ``row-content-disagrees``: a file's duration, codec, sample rate or channels disagree with its row;
* ``tags-name-another-book``: a file's album or title tag, normalised (case, accents and punctuation folded,
  numbers dropped), is another library book's whole distinctive title, alone or wrapped only in release words
  ("(Unabridged)", "Book 2:", ": A Novel") or that book's author's name (``tag_is_title``). Containing it is
  not enough: PLAN's token-subset veto (``matcher.other_book_named``) was found to fire mostly on loose and
  one-word matches (answers/28), so it is not used here. Never waived: PLAN's waiver rests on the file being
  the row's own object, while the open question for a quarantined book is whether that object was ever the
  right content;
* ``tags-loose-match-other-book``: no tag is another book's title by the rule above, but the album and title
  tags together hold every word of another library book's title (PLAN's ``matcher.other_book_named``, the
  round-3 rule). answers/28 found such hits mostly false, so this is not evidence the content is another book;
  but series prefixes, "by Author", narrator credits and other languages wrap real foreign titles too, and no
  list of wrappers is complete. Never restorable: a list for manual review;
* ``asin-tag-not-own``: a file carries an ASIN tag that is not the book's ``Asin``;
* ``no-asin`` / ``source-folder-id-not-asin``: the files' folder under --restore-from does not carry the book's
  ``Asin`` as its bracketed id;
* ``several-source-folders``: the book's files are spread over several such folders;
* ``runtime-misfit``: the summed duration of the book's files (restored, other stale rows' objects under the
  root, live rows) is more than 10 % off the catalog runtime. Audio beside the quarantined files that no row
  holds (rows the service dropped) is restored with them and counted here, so a merged-in second copy fails;
* ``runtime-unknown`` / ``runtime-near``: the catalog runtime is missing, a duration could not be read, or the
  sum is within 10 % but not within max(60 s, 1 %): the runtime does not positively confirm the book;
* ``content-unconfirmed``: some file's album/title tags do not carry this book's whole (distinctive) title. A
  file merely not naming another library book is not evidence (most wrong-content grabs are works outside the
  library), and neither is an ASIN tag equal to the book's ``Asin``: Listenarr's manual import writes the target
  book's ASIN into every file it imports, whatever the audio is. Those files are counted as ``asin-only``;
* ``no-target-folder`` / ``several-target-folders``: no single folder under a root carries the book's ``Asin``
  (``BasePath`` wins when it is one of them);
* ``target-holds-other-audio``: that folder already holds audio no row of this book holds (a second copy, or
  another book's files: restoring would recreate a merged folder);
* ``target-name-taken``: a destination name exists already, or two files would land on one name;
* ``cross-device``: a file is not on the target folder's device (the move would be a copy, a new object);
* ``hardlinked-elsewhere``: a file has another name that is not under a --restore-from or --quarantine-dir
  folder in a folder with the book's id (PLAN would then refuse the re-link after the move);
* ``object-shared``: a file would go back for two books (both are refused).

Two classes come from what the operator's records say about each quarantine entry (``--labels``: one line per
top-level entry under --restore-from, ``<entry name><TAB><label>``, filled from highside's records and never sent):

* ``in-root-copy``: (unless the entry's label says it was removed on purpose, which wins) the target folder
  already holds a copy of every quarantined file: duration within 1 s and the same audio-stream hash, one to
  one. Size is not compared: a retagged copy is still a copy. The quarantine was a de-duplication and the
  library keeps the other copy: re-point the rows at it rather than restore a duplicate. This overrides any
  other refusal, since it says what to do instead;
* ``operator-re-review``: a book that passes every check but sits in an entry the operator removed on purpose
  (``wrong-content``, ``wrong-asin``, ``edition-audit``, ``manual-review``, ``empty-stub``): the only books whose
  content is confirmed and which only the removal holds back. Never restored by this tool: the operator looks
  at the removal again;
* ``deliberate-removal``: a removed-on-purpose book whose target already holds a copy of its audio;
* ``unlabelled``: a book that passes every check but whose entry has no label. Fails closed.

A book with no file row at all (the service dropped its rows) is a candidate when exactly one folder under
--restore-from carries its ``Asin``; all its audio is restored, under the same checks.
Registering those files is left to the service (a per-book scan of a book with no rows removes nothing).
"""

from __future__ import annotations

import json
import logging
import os
import re
import sqlite3
import stat
from collections import Counter, defaultdict
from collections.abc import Callable
from dataclasses import dataclass, field
from datetime import UTC, datetime
from functools import partial
from pathlib import Path, PurePosixPath
from typing import Any

from listenarr_relink import TOOL_NAME, identity, media, physical
from listenarr_relink.inventory import (
    BOOK_COLUMNS,
    BookRow,
    DiskFile,
    FileRow,
    Library,
    QuarantineName,
    lstat_or_none,
    open_read_only,
    row_digest,
)
from listenarr_relink.matcher import (
    _consistency,
    _parallel,
    _resolves,
    match,
    other_book_named,
    own_title_tagged,
    root_of,
    tag_tokens,
    veto_keys,
)
from listenarr_relink.paths import PathMap, is_audio, is_distinctive, is_within, tokens
from listenarr_relink.plan_io import file_sha256

log = logging.getLogger("relink.qrestore")

RESTORE_PLAN_FORMAT = 1
RESTORABLE = "restorable"
# Quarantine entry labels the operator may give (--labels); anything else refuses the run, so no free text from a
# highside record can reach the counts. The first group put aside a second copy; the second removed content on purpose.
DUPLICATE_LABELS = ("doubled-merge", "series-collision", "same-asin-dedup")
DELIBERATE_LABELS = ("wrong-content", "wrong-asin", "edition-audit", "manual-review", "empty-stub")
LABELS = (*DUPLICATE_LABELS, *DELIBERATE_LABELS)
UNLABELLED = "unlabelled"
BOOK_CLASSES = ("restorable", "not-every-row-found", "row-content-disagrees", "tags-name-another-book",
                "tags-loose-match-other-book",
                "asin-tag-not-own", "no-asin", "source-folder-id-not-asin", "several-source-folders",
                "runtime-misfit", "runtime-unknown", "runtime-near", "content-unconfirmed", "no-target-folder",
                "several-target-folders", "target-holds-other-audio", "target-name-taken", "cross-device",
                "hardlinked-elsewhere", "object-shared", "in-root-copy", "operator-re-review", "deliberate-removal",
                UNLABELLED)
FAILURES = (*(name for name in BOOK_CLASSES if name not in (RESTORABLE, "in-root-copy", "operator-re-review",
                                                            "deliberate-removal", UNLABELLED)),
            "deliberate-label", UNLABELLED)
# Checks about what the audio is (not where it goes): a later re-point design must look at these first.
CONTENT_CHECKS = ("row-content-disagrees", "tags-name-another-book", "tags-loose-match-other-book",
                  "asin-tag-not-own", "runtime-misfit", "runtime-unknown", "runtime-near", "content-unconfirmed")
RUNTIME_FIT_SECONDS = 60.0
RUNTIME_FIT_SHARE = 0.01
RUNTIME_NEAR_SHARE = 0.10
ASIN_TAG_KEYS = ("asin", "audible_asin", "cdek")
_BRACKETED_ID = re.compile(r"\[([^\[\]]+)\]\s*$")


def title_key(text: str) -> tuple[str, ...]:
    """A title normalised for an exact comparison: folded word tokens in order, pure numbers dropped."""
    return tuple(token for token in tokens(text) if not token.isdigit())


# Words a release may wrap a title in without naming another work: "(Unabridged)", "Book 2:", ": A Novel", "Part 3".
# Applies to titles of two or more words; a one-word title must stand alone (bar these few), since a tag such as
# "<word> chapter 01" or "<word> Notes" is the loose match answers/28 found to be mostly false.
TITLE_WRAPPERS = frozenset({"unabridged", "abridged", "audiobook", "audio", "edition", "book", "books", "volume",
                            "vol", "part", "pt", "disc", "disk", "cd", "track", "chapter", "a", "novel", "the"})
ONE_WORD_TITLE_WRAPPERS = frozenset({"unabridged", "abridged", "audiobook"})


def tag_is_title(tag: str, key: tuple[str, ...], wrapper_words: frozenset[str]) -> bool:
    """True when the tag is this title, possibly wrapped in release words or the work's author's name.

    The title's words must appear together and in order; everything else in the tag must be a number, a
    wrapper word or one of ``wrapper_words`` (the named book's author tokens). Exact equality is the plain case.
    """
    words = title_key(tag)
    allowed = (TITLE_WRAPPERS | wrapper_words) if len(key) > 1 else ONE_WORD_TITLE_WRAPPERS
    for start in range(len(words) - len(key) + 1):
        if words[start:start + len(key)] == key:
            rest = words[:start] + words[start + len(key):]
            if all(word in allowed for word in rest):
                return True
    return False


def title_keys(books: dict[int, BookRow]) -> dict[tuple[str, ...], int]:
    """Normalised whole title of every distinctive library title -> the first book holding it."""
    keys: dict[tuple[str, ...], int] = {}
    for book in books.values():
        if is_distinctive(book.title):
            keys.setdefault(title_key(book.title), book.id)
    return keys


def own_folder_id(folder: str) -> str | None:
    """The bracketed id ending this folder's own name, casefolded (not a parent's)."""
    found = _BRACKETED_ID.search(PurePosixPath(folder).name)
    return found.group(1).strip().casefold() if found else None


def book_folder(path: str, top: str) -> str | None:
    """The nearest folder above ``path``, below ``top``, whose own name carries a bracketed id."""
    parent = PurePosixPath(path).parent
    while is_within(str(parent), top) and str(parent).rstrip("/") != top.rstrip("/"):
        if own_folder_id(str(parent)) is not None:
            return str(parent)
        parent = parent.parent
    return None


def runtime_fit(seconds: float | None, runtime_minutes: str | None) -> str:
    """Duration against catalog runtime: fits (max(60 s, 1 %)), near (10 %), misfit, or unknown (either missing)."""
    try:
        expected = float(runtime_minutes or "") * 60
    except ValueError:
        return "unknown"
    if seconds is None or expected <= 0:
        return "unknown"
    difference = abs(seconds - expected)
    if difference <= max(RUNTIME_FIT_SECONDS, RUNTIME_FIT_SHARE * expected):
        return "fits"
    return "near" if difference <= RUNTIME_NEAR_SHARE * expected else "misfit"


def asin_tags(tags: dict[str, str]) -> set[str]:
    """ASIN values the file's tags carry, casefolded."""
    return {value.strip().casefold() for key, value in tags.items()
            if (key in ASIN_TAG_KEYS or key.endswith(":asin")) and value.strip()}


@dataclass
class RestoreFile:
    """One file to move back: from its quarantine name to its destination under a root (host paths)."""

    source: DiskFile
    destination: str
    row_id: int | None
    probe: media.Probe | None = None


@dataclass
class RestoreBook:
    """One book's restore verdict, with the evidence counted in the summary."""

    book: BookRow
    kind: str  # "rows" (stale rows whose objects are quarantined) or "zero-rows" (the book has no file row)
    files: list[RestoreFile] = field(default_factory=list)
    verdict: str = RESTORABLE
    detail: str = ""
    target: str | None = None  # service spelling of the destination folder
    target_host: str | None = None
    runtime: str = "unknown"
    target_audio_runtime: str | None = None  # runtime fit of the audio already in the target, when there is some
    own_evidence: int = 0  # files whose album/title tags carry this book's whole title (independent evidence)
    asin_only: int = 0  # files naming the book only by an own ASIN tag: manual import writes that tag itself
    label: str = UNLABELLED  # the operator's label of the quarantine entry holding the files
    in_root_copy: bool = False  # the target already holds a copy of every file (duration and audio hash)
    # (quarantined file, its in-root copy, the shared audio-stream hash), when in_root_copy
    copy_pairs: list[tuple[RestoreFile, DiskFile, str]] = field(default_factory=list)
    blocklisted: bool = False
    target_audio: list[str] = field(default_factory=list)  # audio already in the target (the book's own live rows)
    target_audio_files: int = 0  # audio in the target that is not this book's (counted when it refuses)
    target_audio_repeats: int = 0  # ... of which a quarantined file has the same duration (the same recording?)
    in_root: list[DiskFile] = field(default_factory=list)  # objects of this book's other stale rows under a root
    live: list[str] = field(default_factory=list)  # host paths of this book's live rows
    # Every check the book fails, in check order (the class is the first); "deliberate-label" when its entry was
    # removed on purpose, "unlabelled" when it has no label: what each fix alone would and would not free.
    failures: list[str] = field(default_factory=list)

    def refuse(self, verdict: str, detail: str) -> None:
        """Record a failing check. The first names the class; every one is kept in ``failures``."""
        if verdict not in self.failures:
            self.failures.append(verdict)
        if self.verdict == RESTORABLE:
            self.verdict, self.detail = verdict, detail


@dataclass
class Survey:
    """Every candidate book, the row-level counts, what the walk saw, and how the labels fit the quarantine."""

    books: list[RestoreBook]
    rows: Counter[str]
    labels: LabelAudit


@dataclass
class LabelAudit:
    """The labels file against the quarantine as walked: what the kit pins against the operator's records."""

    entries: int = 0  # top-level entries under the --restore-from folders
    matched: int = 0  # entries with a label line
    lines_unmatched: int = 0  # label lines naming no entry (a typo, or another quarantine)
    stale_rows: Counter[str] = field(default_factory=Counter)  # quarantined objects of stale rows, by label
    folders_with_files: Counter[str] = field(default_factory=Counter)  # by label

    @property
    def consistent(self) -> bool:
        """Every entry labelled and every line used: the only state a restore plan is built from."""
        return self.entries == self.matched and self.lines_unmatched == 0

    def lines(self) -> list[str]:
        """Counts only: the fixed labels and numbers, never an entry name."""
        return [f"label_entries {self.entries}", f"label_entries_matched {self.matched}",
                f"label_lines_unmatched {self.lines_unmatched}",
                *(f"stale_rows_by_label {label} {self.stale_rows[label]}" for label in (*LABELS, UNLABELLED)),
                *(f"folders_with_files_by_label {label} {self.folders_with_files[label]}"
                  for label in (*LABELS, UNLABELLED))]


def audit_labels(library: Library, labels: dict[str, str]) -> LabelAudit:
    """Match label lines to top-level entries, and count folders holding files per label."""
    audit = LabelAudit()
    names: set[str] = set()
    for top in library.restore_dirs:
        for entry in os.listdir(top):
            names.add(entry)
    audit.entries, audit.matched = len(names), len(names & set(labels))
    audit.lines_unmatched = len(set(labels) - names)
    for folder in library.restore_folders:
        top = next(d for d in library.restore_dirs if is_within(folder, d))
        if folder.rstrip("/") == top.rstrip("/"):
            continue
        try:
            holds_files = any(entry.is_file(follow_symlinks=False) for entry in os.scandir(folder))
        except OSError:
            continue
        if holds_files:
            entry_name = PurePosixPath(os.path.relpath(folder, top)).parts[0]
            audit.folders_with_files[labels.get(entry_name, UNLABELLED)] += 1
    return audit


def _host_roots(library: Library, path_map: PathMap) -> list[str]:
    return [p for r in library.roots for p in (path_map.to_host(r), os.path.realpath(path_map.to_host(r)))]


def _blocklisted_books(db_file: str | None) -> set[int]:
    """Books with a BlockedReleases row (a release for them was refused once), when the table exists."""
    if db_file is None:
        return set()
    connection = open_read_only(db_file)
    try:
        if not connection.execute("SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'BlockedReleases'"
                                  ).fetchone():
            return set()
        return {int(r[0]) for r in connection.execute("SELECT DISTINCT AudiobookId FROM BlockedReleases")}
    finally:
        connection.close()


def read_labels(path: str) -> dict[str, str]:
    """``<entry name><TAB><label>`` lines (blank and ``#`` lines ignored); ValueError on an unknown label."""
    labels: dict[str, str] = {}
    with open(path, encoding="utf-8", errors="surrogateescape") as handle:
        for number, line in enumerate(handle, start=1):
            if not line.strip() or line.startswith("#"):
                continue
            name, sep, label = line.rstrip("\n").rpartition("\t")
            if not sep or label.strip() not in LABELS:
                raise ValueError(f"labels line {number}: expected '<entry><TAB><one of {', '.join(LABELS)}>'")
            labels[name] = label.strip()
    return labels


def survey(library: Library, path_map: PathMap, workers: int, db_file: str | None = None,
           labels: dict[str, str] | None = None) -> Survey:
    """Classify every book whose stale rows' objects, or whose only folder, lie under a --restore-from folder."""
    rows: Counter[str] = Counter()
    host_roots = _host_roots(library, path_map)
    files_by_book: dict[int, list[FileRow]] = defaultdict(list)
    for row in library.files:
        files_by_book[row.audiobook_id].append(row)
    stale = [r for r in library.files
             if r.path and not _resolves(r.path, path_map) and lstat_or_none(path_map.to_host(r.path)) is None]
    stale_ids = {r.id for r in stale}
    claimed = {(info.st_dev, info.st_ino) for r in library.files if r.path and r.id not in stale_ids
               for info in [lstat_or_none(path_map.to_host(r.path))] if info is not None}
    in_source: dict[int, list[DiskFile]] = defaultdict(list)
    for source_file in library.restore_source:
        in_source[source_file.ino].append(source_file)
        if source_file.nlink > 1:
            # A restore folder's linked names count as quarantine names, as the PLAN after the move will be told.
            library.quarantine.setdefault(source_file.ino, []).append(QuarantineName(
                source_file.host_path, _restore_dir_of(source_file, library), source_file.dev, source_file.ino))
    devices_under_roots = frozenset({f.dev for f in library.disk} | set(library.root_devices.values()))
    other_names: dict[int, list[str]] = defaultdict(list)
    for names in library.quarantine.values():
        for name in names:
            other_names[name.ino].append(name.host_path)
    # What PLAN decides today for the rows whose objects are under a root: their books may be restored only
    # when PLAN would re-link those rows too, once the quarantined siblings are back (book-atomic rule).
    plan_today = {d.row.id: d for d in match(library, path_map, workers, hash_chosen=False, identity_only=True,
                                             tags_waiver="none").decisions}

    def root_device_of(row: FileRow) -> int | None:
        root = root_of(row.path, library.roots)
        return library.root_devices.get(root) if root is not None else None

    def object_of(row: FileRow, same_inode: list[DiskFile]) -> physical.Verdict:
        stored = identity.parse_stored(str(row.values.get("PhysicalObjectIdentity") or ""))
        assert stored is not None
        return physical.classify(stored, same_inode, identity.read_live, partial(_root_device, root_device_of, row),
                                 devices_under_roots, library.device_pools, _accept_links)

    candidates: dict[int, RestoreBook] = {}
    label_audit = audit_labels(library, labels or {})
    in_root: dict[int, list[DiskFile]] = defaultdict(list)
    accounted: set[int] = set()
    for row in stale:
        stored = identity.parse_stored(str(row.values.get("PhysicalObjectIdentity") or ""))
        if stored is None:
            rows["no-stored-identity"] += 1
            continue
        decision = plan_today.get(row.id)
        found = decision.candidates[0] if decision is not None and decision.candidates else None
        if found is not None and found.identity_match:
            # The object has a name under the root: PLAN's case, not a restore. It does not hold its book back
            # when PLAN links it, or withholds it only because its quarantined siblings are still away.
            if decision is not None and (decision.chosen is not None or "partly linked" in str(decision.reason)):
                rows["object-under-a-root-plan-links"] += 1
                in_root[row.audiobook_id].append(found.file)
                accounted.add(row.id)
            else:
                rows["object-under-a-root-plan-refuses"] += 1
            continue
        same_inode = [f for f in in_source.get(stored.inode, []) if (f.dev, f.ino) not in claimed]
        if not same_inode:
            rows["object-nowhere-walked"] += 1
            continue
        verdict = object_of(row, same_inode)
        book = library.books.get(row.audiobook_id)
        if book is None:
            rows["no-book"] += 1
            continue
        restore = candidates.setdefault(book.id, RestoreBook(book, "rows"))
        if verdict.kind != physical.MATCH:
            rows[f"object-{verdict.kind}"] += 1
            restore.refuse("not-every-row-found", f"row {row.id}: {verdict.kind}")
            continue
        rows["object-in-restore-source"] += 1
        (hit,) = verdict.hits
        entry_name = PurePosixPath(os.path.relpath(hit.host_path, _restore_dir_of(hit, library))).parts[0]
        label_audit.stale_rows[(labels or {}).get(entry_name, UNLABELLED)] += 1
        rows["nlink-1" if hit.nlink == 1 else "nlink-over-1"] += 1
        restore.files.append(RestoreFile(hit, "", row.id))
        accounted.add(row.id)
    in_library = claimed | {(f.dev, f.ino) for f in library.disk}
    for book_id, restore in candidates.items():
        missing = [r.id for r in files_by_book[book_id] if r.id in stale_ids and r.id not in accounted]
        if missing:
            restore.refuse("not-every-row-found", f"{len(missing)} stale row(s) whose object was not found")
        restore.in_root = in_root.get(book_id, [])
        restore.live = [path_map.to_host(r.path) for r in files_by_book[book_id] if r.id not in stale_ids and r.path]
        rows["books-with-a-row-under-a-root"] += bool(restore.in_root)
        rows["books-with-a-live-row"] += bool(restore.live)
        _add_unregistered(restore, library, rows, in_library)

    folders_by_id: dict[str, list[str]] = defaultdict(list)
    for folder in library.restore_folders:
        if (folder_id := own_folder_id(folder)) is not None:
            folders_by_id[folder_id].append(folder)
    for book in library.books.values():
        asin = book.context.get("Asin", "").strip().casefold()
        if files_by_book.get(book.id) or not asin or asin not in folders_by_id:
            continue
        restore = candidates.setdefault(book.id, RestoreBook(book, "zero-rows"))
        sources = folders_by_id[asin]
        if len(sources) > 1:
            restore.refuse("several-source-folders", f"{len(sources)} folders under --restore-from carry its id")
            continue
        restore.files = [RestoreFile(f, "", None) for f in library.restore_source if is_within(f.host_path, sources[0])]

    paths = [*(f.source.host_path for b in candidates.values() for f in b.files),
             *(f.host_path for b in candidates.values() for f in b.in_root),
             *(p for b in candidates.values() for p in b.live)]
    probes = _parallel(paths, media.probe, workers)
    blocklisted = _blocklisted_books(db_file)
    other_titles = title_keys(library.books)
    loose_titles = veto_keys(library.books)
    rows_by_id = {r.id: r for r in library.files}
    books_of_object: dict[tuple[int, int], set[int]] = defaultdict(set)
    for restore in candidates.values():
        for item in restore.files:
            books_of_object[(item.source.dev, item.source.ino)].add(restore.book.id)
    for restore in candidates.values():
        if any(len(books_of_object[(f.source.dev, f.source.ino)]) > 1 for f in restore.files):
            restore.refuse("object-shared", "a file would go back for another book too")
        for item in restore.files:
            probe = probes[item.source.host_path]
            assert isinstance(probe, media.Probe)
            item.probe = probe
        restore.blocklisted = restore.book.id in blocklisted
        restore.label = _label_of(restore, library, labels or {})
        _check(restore, library, path_map, host_roots, rows_by_id, other_titles, loose_titles, other_names, rows,
               probes)
        if restore.label in DELIBERATE_LABELS:
            restore.failures.append("deliberate-label")
        elif restore.label == UNLABELLED:
            restore.failures.append(UNLABELLED)
        if restore.label in DELIBERATE_LABELS and restore.verdict == RESTORABLE and not restore.in_root_copy:
            # Passes every check, held only by the operator's removal: the operator looks again. Never restorable.
            restore.verdict = "operator-re-review"
            restore.detail = f"passes every check; removed on purpose ({restore.label})"
        elif restore.label in DELIBERATE_LABELS and restore.in_root_copy:
            # Removed on purpose: re-pointing rows at an in-root copy of the same audio would keep the mistake.
            restore.verdict, restore.detail = "deliberate-removal", f"removed on purpose ({restore.label}): review"
        elif restore.in_root_copy:
            restore.verdict, restore.detail = "in-root-copy", "the target already holds a copy of every file"
        elif restore.verdict == RESTORABLE and restore.label == UNLABELLED:
            restore.verdict, restore.detail = UNLABELLED, "its quarantine entry has no label"
    return Survey(sorted(candidates.values(), key=lambda b: b.book.id), rows, label_audit)


def _label_of(restore: RestoreBook, library: Library, labels: dict[str, str]) -> str:
    """The label of the one top-level quarantine entry holding the book's files (unlabelled if none or several)."""
    entries = {PurePosixPath(os.path.relpath(f.source.host_path, _restore_dir_of(f.source, library))).parts[0]
               for f in restore.files}
    found = {labels.get(entry, UNLABELLED) for entry in entries}
    return found.pop() if len(found) == 1 else UNLABELLED


def _add_unregistered(restore: RestoreBook, library: Library, rows: Counter[str],
                      in_library: set[tuple[int, int]]) -> None:
    """Add the audio beside a book's quarantined files that no row holds: files whose rows the service dropped.

    Only from the one folder carrying the book's id, and never an object that also has a name under a root or
    a row's path. Every check then applies to them too, the tag veto never waived (no identity), and the runtime
    check covers the whole set, so a second copy makes it misfit.
    """
    folders = {book_folder(f.source.host_path, _restore_dir_of(f.source, library)) for f in restore.files}
    if len(folders) != 1 or None in folders:
        return
    (folder,) = folders
    objects = {(f.source.dev, f.source.ino) for f in restore.files}
    for source_file in library.restore_source:
        key = (source_file.dev, source_file.ino)
        if is_within(source_file.host_path, str(folder)) and key not in objects and key not in in_library:
            restore.files.append(RestoreFile(source_file, "", None))
            rows["unregistered-files-beside"] += 1


def _root_device(root_device_of: Callable[[FileRow], int | None], row: FileRow, _file: DiskFile) -> int | None:
    return root_device_of(row)


def _accept_links(_file: DiskFile) -> bool:
    """Every other link is accepted here; ``_place`` decides whether PLAN will accept it after the move."""
    return True


def _check(restore: RestoreBook, library: Library, path_map: PathMap, host_roots: list[str],
           rows_by_id: dict[int, FileRow], other_titles: dict[tuple[str, ...], int],
           loose_titles: dict[frozenset[str], int],
           other_names: dict[int, list[str]], rows: Counter[str],
           probes: dict[str, object]) -> None:
    book = restore.book
    asin = book.context.get("Asin", "").strip().casefold()
    own_key = title_key(book.title)
    others = {k: v for k, v in other_titles.items() if v != book.id and k != own_key}
    loose_others = {k: v for k, v in loose_titles.items() if v != book.id}
    if not restore.files:
        restore.refuse("not-every-row-found", "no audio file to restore")
    for item in restore.files:
        probe = item.probe
        assert probe is not None
        row = rows_by_id.get(item.row_id) if item.row_id is not None else None
        if probe.error:
            restore.refuse("row-content-disagrees", f"not readable as audio: {probe.error}")
            continue
        if row is not None:
            reason = _consistency(row, probe, {}, book, True)
            rows["content-agrees" if reason is None else "content-disagrees"] += 1
            if reason is not None:
                restore.refuse("row-content-disagrees", f"row {row.id}: {reason}")
        named = _tags_name_another(probe.tags, others, library)
        if named is not None:
            rows["tags-name-another-book"] += 1
            restore.refuse("tags-name-another-book", f"tags name audiobook {named}")
        elif (loose := other_book_named(probe.tags, loose_others, book, row is not None)) is not None:
            # PLAN's token-subset hit: mostly false (answers/28), so not evidence, but no wrapper list can be
            # complete either. It never restores: the book goes to manual review instead.
            rows["tags-loose-match-other-book"] += 1
            restore.refuse("tags-loose-match-other-book", f"tags contain audiobook {loose}'s title")
        tagged = asin_tags(probe.tags)
        rows["asin-tag-absent" if not tagged else "asin-tag-own" if tagged == {asin} else "asin-tag-other"] += 1
        if tagged and tagged != {asin}:
            restore.refuse("asin-tag-not-own", "an ASIN tag is not the book's Asin")
        title_own = own_title_tagged(tag_tokens(probe.tags), book)
        rows["own-title-tagged" if title_own else "own-title-not-tagged"] += 1
        if title_own:
            restore.own_evidence += 1
        elif bool(asin) and tagged == {asin}:
            restore.asin_only += 1
    if not asin:
        restore.refuse("no-asin", "the book has no Asin")
    source_folders = {book_folder(f.source.host_path, _restore_dir_of(f.source, library)) for f in restore.files}
    if None in source_folders or any(own_folder_id(str(f)) != asin for f in source_folders):
        restore.refuse("source-folder-id-not-asin", "a file's folder under --restore-from does not carry the Asin")
    elif len(source_folders) > 1:
        restore.refuse("several-source-folders", f"the files lie in {len(source_folders)} folders")
    durations = [f.probe.duration if f.probe else None for f in restore.files]
    durations += [_duration(probes, path) for path in [*(f.host_path for f in restore.in_root), *restore.live]]
    total = None if not restore.files or None in durations else sum(d for d in durations if d is not None)
    restore.runtime = runtime_fit(total, book.context.get("Runtime"))
    if restore.runtime == "misfit":
        restore.refuse("runtime-misfit", "summed duration is more than 10 % off the catalog runtime")
    elif restore.runtime == "unknown":
        restore.refuse("runtime-unknown", "no catalog runtime, or a duration could not be read")
    elif restore.runtime == "near":
        restore.refuse("runtime-near", "summed duration is within 10 % but not within max(60 s, 1 %)")
    if restore.own_evidence < len(restore.files):
        restore.refuse("content-unconfirmed", f"{len(restore.files) - restore.own_evidence} file(s) do not name "
                                              "this book (own title in the tags, or own ASIN tag)")
    _choose_target(restore, library, path_map, asin, rows_by_id)
    _place(restore, library, path_map, host_roots, other_names, asin)


def _tags_name_another(tags: dict[str, str], others: dict[tuple[str, ...], int], library: Library) -> int | None:
    """The other library book whose whole title a file's album or title tag is (``tag_is_title``), or None."""
    for tag in ("album", "title"):
        text = tags.get(tag, "")
        if not text:
            continue
        exact = others.get(title_key(text))
        if exact is not None:
            return exact
        present = set(title_key(text))
        for key, book_id in others.items():
            if key[0] in present and set(key) <= present:  # cheap pre-filter before the ordered match
                other = library.books.get(book_id)
                author_words = frozenset(t for a in (other.authors if other else []) for t in tokens(a))
                if tag_is_title(text, key, author_words):
                    return book_id
    return None


def _duration(probes: dict[str, object], path: str) -> float | None:
    probe = probes.get(path)
    return probe.duration if isinstance(probe, media.Probe) and not probe.error else None


def _restore_dir_of(source: DiskFile, library: Library) -> str:
    return next(d for d in library.restore_dirs if is_within(source.host_path, d))


def _choose_target(restore: RestoreBook, library: Library, path_map: PathMap, asin: str,
                   rows_by_id: dict[int, FileRow]) -> None:
    targets = [f for f in library.folders if asin and own_folder_id(f) == asin]
    base = restore.book.base_path.rstrip("/")
    if base in targets:
        targets = [base]
    if not targets:
        restore.refuse("no-target-folder", "no folder under a root carries the book's Asin")
        return
    if len(targets) > 1:
        restore.refuse("several-target-folders", f"{len(targets)} folders under the roots carry the book's Asin")
        return
    restore.target = targets[0]
    restore.target_host = path_map.to_host(targets[0])
    held = {r.path for r in rows_by_id.values() if r.audiobook_id == restore.book.id}
    own = {(f.dev, f.ino) for f in restore.in_root}
    present = [f for f in library.disk if is_within(f.db_path, targets[0]) and f.db_path not in held
               and (f.dev, f.ino) not in own]
    if present:
        probes = [media.probe(f.host_path) for f in present]
        durations = [p.duration for p in probes]
        restore.target_audio_runtime = runtime_fit(None if None in durations else sum(d or 0.0 for d in durations),
                                                   restore.book.context.get("Runtime"))
        quarantined = [f.probe.duration for f in restore.files if f.probe and f.probe.duration is not None]
        restore.target_audio_repeats = sum(1 for d in durations if d is not None and any(
            abs(d - q) <= max(1.0, 0.005 * q) for q in quarantined))
        restore.target_audio_files = len(present)
        restore.copy_pairs = _copy_pairs(restore.files, present, durations) or []
        restore.in_root_copy = bool(restore.copy_pairs)
        restore.refuse("target-holds-other-audio", f"the target already holds {len(present)} audio file(s)")
    restore.target_audio = sorted(os.path.relpath(f.host_path, restore.target_host) for f in library.disk
                                  if is_within(f.db_path, targets[0]))


def _copy_pairs(files: list[RestoreFile], present: list[DiskFile],
                durations: list[float | None]) -> list[tuple[RestoreFile, DiskFile, str]] | None:
    """Each file paired, one to one, with a present file of the same duration (1 s) and audio-stream hash.

    The hash covers the audio packets only, so a copy that was retagged (manual import writes an ASIN tag) still
    pairs; size is not compared, since a tag edit changes it. Equal lengths alone happen between chapters of one
    constant-bitrate book, which is why the hash, not the duration, decides. None when any file has no partner.
    """
    free = list(zip(present, durations, strict=True))
    hashes: dict[str, str | None] = {}
    pairs: list[tuple[RestoreFile, DiskFile, str]] = []

    def audio(path: str) -> str | None:
        if path not in hashes:
            hashes[path] = media.audio_hash(path)
        return hashes[path]

    for item in files:
        duration = item.probe.duration if item.probe else None
        match_at = next((i for i, (other, other_duration) in enumerate(free)
                         if duration is not None and other_duration is not None
                         and abs(other_duration - duration) <= 1.0
                         and audio(other.host_path) is not None
                         and audio(other.host_path) == audio(item.source.host_path)), None)
        if match_at is None:
            return None
        copy, _ = free.pop(match_at)
        pairs.append((item, copy, str(audio(copy.host_path))))
    return pairs if files else None


def _place(restore: RestoreBook, library: Library, path_map: PathMap, host_roots: list[str],
           other_names: dict[int, list[str]], asin: str) -> None:
    if restore.target_host is None:
        return
    target_device = os.lstat(restore.target_host).st_dev
    destinations: set[str] = set()
    for item in restore.files:
        if item.source.dev != target_device:
            restore.refuse("cross-device", "a file is on another device than the target folder")
        top = _restore_dir_of(item.source, library)
        folder = book_folder(item.source.host_path, top)
        relative = os.path.relpath(item.source.host_path, folder) if folder else os.path.basename(item.source.host_path)
        item.destination = os.path.join(restore.target_host, relative)
        if item.destination in destinations or os.path.lexists(item.destination):
            restore.refuse("target-name-taken", "a destination name exists or is used twice")
        destinations.add(item.destination)
        if item.source.nlink > 1:
            names = [n for n in other_names.get(item.source.ino, []) if n != item.source.host_path]
            relinkable = len(names) == item.source.nlink - 1 and all(
                not any(is_within(n, r) for r in host_roots)
                and own_folder_id(book_folder(n, next((d for d in [*library.restore_dirs, *library.quarantine_dirs]
                                                       if is_within(n, d)), "/")) or "") == asin
                for n in names)
            if not relinkable:
                restore.refuse("hardlinked-elsewhere", "a file has another name PLAN would not accept")


def ctime_days(survey_result: Survey) -> Counter[str]:
    """UTC day of each candidate file's ctime (a rename into the quarantine set it): when the files were moved."""
    return Counter(datetime.fromtimestamp(f.source.ctime_ns / 1e9, UTC).strftime("%Y-%m-%d")
                   for b in survey_result.books for f in b.files)


def summary_lines(library: Library, result: Survey) -> list[str]:
    """Counts only: never a title, a name or a path."""
    classes = Counter(b.verdict for b in result.books)
    restorable = [b for b in result.books if b.verdict == RESTORABLE]
    holding = [b for b in result.books if b.target_audio_files]
    lines = [
        f"restore_from_dirs {len(library.restore_dirs)}",
        f"restore_source_audio_files {len(library.restore_source)}",
        f"restore_source_folders_with_id {sum(1 for f in library.restore_folders if own_folder_id(f))}",
        f"walk_errors {len(library.walk_errors)}",
        "rows (stale rows, by where their object is):",
        *(f"  {name} {count}" for name, count in sorted(result.rows.items())),
        f"books_rows {sum(1 for b in result.books if b.kind == 'rows')}",
        f"books_zero_rows {sum(1 for b in result.books if b.kind == 'zero-rows')}",
        "book classes (each candidate book in exactly one):",
        *(f"  {name} {classes[name]}" for name in BOOK_CLASSES),
        f"restorable_books_rows {sum(1 for b in restorable if b.kind == 'rows')}",
        f"restorable_books_zero_rows {sum(1 for b in restorable if b.kind == 'zero-rows')}",
        *(f"runtime_{fit} {sum(1 for b in result.books if b.runtime == fit)}"
          for fit in ("fits", "near", "misfit", "unknown")),
        *(f"target_other_audio_runtime_{fit} {sum(1 for b in result.books if b.target_audio_runtime == fit)}"
          for fit in ("fits", "near", "misfit", "unknown")),
        "target_other_audio_books_by_repeats (every file repeats a quarantined duration / some / none):",
        f"  all {sum(1 for b in holding if b.target_audio_repeats == b.target_audio_files)}",
        f"  some {sum(1 for b in holding if 0 < b.target_audio_repeats < b.target_audio_files)}",
        f"  none {sum(1 for b in holding if b.target_audio_repeats == 0)}",
        f"basepath_is_target {sum(1 for b in result.books if b.target and b.book.base_path.rstrip('/') == b.target)}",
        *result.labels.lines(),
        "failing_checks (books failing each check; a book counts once per check it fails):",
        *(f"  {name} {sum(1 for b in result.books if name in b.failures)}" for name in FAILURES),
        "books_failing_only (the one check a book fails: what fixing that check alone would free):",
        *(f"  {name} {sum(1 for b in result.books if b.failures == [name])}" for name in FAILURES),
        "label_class (operator label, class: books):",
        *(f"  {label} {name} {count}" for (label, name), count in sorted(Counter(
            (b.label, b.verdict) for b in result.books).items())),
        f"books_with_an_in_root_copy {sum(1 for b in result.books if b.in_root_copy)}",
        "in_root_copy_books_with_a_content_failure "
        f"{sum(1 for b in result.books if b.in_root_copy and set(b.failures) & set(CONTENT_CHECKS))}",
        "restorable_runtime (fits only, by construction): "
        f"{sum(1 for b in restorable if b.runtime == 'fits')}",
        f"restorable_books_by_evidence title {sum(1 for b in restorable if b.own_evidence == len(b.files))}",
        f"books_with_asin_only_files {sum(1 for b in result.books if b.asin_only)}",
        "books_by_files_naming_this_book (every file / some / none):",
        f"  all {sum(1 for b in result.books if b.files and b.own_evidence == len(b.files))}",
        f"  some {sum(1 for b in result.books if 0 < b.own_evidence < len(b.files))}",
        f"  none {sum(1 for b in result.books if b.files and b.own_evidence == 0)}",
        f"books_with_a_blocklisted_release {sum(1 for b in result.books if b.blocklisted)}",
        f"books_with_a_blocklisted_release_restorable {sum(1 for b in restorable if b.blocklisted)}",
        f"restorable_files {sum(len(b.files) for b in restorable)}",
        f"restorable_rows {sum(1 for b in restorable for f in b.files if f.row_id is not None)}",
        f"restorable_files_with_other_links {sum(1 for b in restorable for f in b.files if f.source.nlink > 1)}",
        f"restorable_files_without_a_row {sum(1 for b in restorable for f in b.files if f.row_id is None)}",
        "restorable_books_rows_needing_a_scan_after_relink "
        f"{sum(1 for b in restorable if b.kind == 'rows' and any(f.row_id is None for f in b.files))}",
        f"restorable_books_with_a_row_under_a_root {sum(1 for b in restorable if b.in_root)}",
        "candidate_file_ctime_days (UTC day: files):",
        *(f"  {day} {count}" for day, count in sorted(ctime_days(result).items())),
    ]
    return lines


def book_lines(result: Survey, with_paths: bool) -> list[str]:
    """One tab-separated line per candidate book: ids and classes (and folders and reason when asked for)."""
    header = ["audiobook_id", "kind", "class", "files", "rows", "runtime", "target_other_audio_runtime",
              "blocklisted", "label", "own_evidence_files", "asin_only_files", "failures"]
    lines = ["\t".join([*header, *(["target", "reason"] if with_paths else [])])]
    for book in result.books:
        cells = [str(book.book.id), book.kind, book.verdict, str(len(book.files)),
                 str(sum(1 for f in book.files if f.row_id is not None)), book.runtime,
                 book.target_audio_runtime or "-", "Y" if book.blocklisted else "N", book.label,
                 str(book.own_evidence), str(book.asin_only), ",".join(book.failures) or "-"]
        if with_paths:
            cells += [book.target or "", book.detail]
        lines.append("\t".join(cell.replace("\t", " ").replace("\n", " ") for cell in cells))
    return lines


def _stat_pin(source: DiskFile) -> dict[str, int]:
    return {"dev": source.dev, "ino": source.ino, "size": source.size, "mtime_ns": source.mtime_ns,
            "ctime_ns": source.ctime_ns, "nlink": source.nlink}


def build_restore_plan(result: Survey, chosen: frozenset[int], library: Library) -> dict[str, Any]:
    """The move plan for the operator's chosen books; ValueError when one of them is not restorable."""
    by_id = {b.book.id: b for b in result.books}
    unknown = sorted(chosen - set(by_id))
    refused = sorted(i for i in chosen if i in by_id and by_id[i].verdict != RESTORABLE)
    if not result.labels.consistent:
        raise ValueError(f"the labels do not match the quarantine: {result.labels.entries - result.labels.matched} "
                         f"entries without a label, {result.labels.lines_unmatched} lines naming no entry")
    if unknown or refused:
        raise ValueError(f"{len(unknown)} chosen book(s) are no candidate and {len(refused)} are not restorable "
                         f"(e.g. {(unknown + refused)[:5]}); choose from the restorable class only")
    books = [by_id[i] for i in sorted(chosen)]
    return {
        "tool": TOOL_NAME,
        "kind": "quarantine-restore",
        "format": RESTORE_PLAN_FORMAT,
        "created_utc": datetime.now(UTC).isoformat(timespec="seconds"),
        "restore_dirs": library.restore_dirs,
        "quarantine_dirs": library.quarantine_dirs,
        "summary": {"books": len(books), "files": sum(len(b.files) for b in books),
                    "rows": sum(1 for b in books for f in b.files if f.row_id is not None),
                    "zero_row_books": sum(1 for b in books if b.kind == "zero-rows")},
        "books": [{
            "audiobook_id": b.book.id,
            "kind": b.kind,
            "target": b.target,
            "target_host": b.target_host,
            "basepath_is_target": b.book.base_path.rstrip("/") == b.target,
            # A per-book scan scans BasePath: a book whose BasePath is not the target gets it set (restore-basepath)
            # before any scan registers its row-less files.
            "basepath_update": None if b.book.base_path.rstrip("/") == b.target else {
                "was": b.book.values.get("BasePath"), "set": b.target,
                "precondition": {"row_sha256": b.book.digest()}},
            "target_audio": b.target_audio,
            "files": [{"row_id": f.row_id, "source": f.source.host_path, "destination": f.destination,
                       "pin": _stat_pin(f.source)} for f in b.files],
        } for b in books],
    }


class BasePathRefused(Exception):
    """A BasePath update failed its precondition; nothing was written."""


def apply_basepaths(db_file: str, plan: dict[str, Any], out_dir: Path, dry_run: bool) -> list[int]:
    """Set each planned book's BasePath to its restore target, in one transaction, or refuse and write nothing.

    Each book row must still equal what the plan read (digest of ``inventory.BOOK_COLUMNS``) and the target folder
    must exist. A dry run still takes the write lock (BEGIN IMMEDIATE), then rolls back. The undo SQL is written
    to ``out_dir`` (``undo-basepath-<stamp>.sql``) before the commit.
    """
    updates = [(b, b["basepath_update"]) for b in plan["books"] if b.get("basepath_update")]
    connection = sqlite3.connect(db_file, timeout=0, isolation_level=None)
    connection.row_factory = sqlite3.Row
    undo: list[str] = []
    refused: list[str] = []
    try:
        try:
            connection.execute("BEGIN IMMEDIATE")
        except sqlite3.OperationalError as error:
            raise BasePathRefused(f"database is locked by another writer: {error}") from error
        for book, update in updates:
            book_id = int(book["audiobook_id"])
            row = connection.execute(f"SELECT {', '.join(BOOK_COLUMNS)} FROM Audiobooks WHERE Id = ?",
                                     (book_id,)).fetchone()
            if row is None or row_digest(dict(row)) != update["precondition"]["row_sha256"]:
                refused.append(f"audiobook {book_id}: row changed since the plan")
                continue
            target = lstat_or_none(str(book["target_host"]))
            if target is None or not stat.S_ISDIR(target.st_mode):
                refused.append(f"audiobook {book_id}: target folder is gone")
                continue
            connection.execute("UPDATE Audiobooks SET BasePath = ? WHERE Id = ?", (update["set"], book_id))
            was = "NULL" if update["was"] is None else "'" + str(update["was"]).replace("'", "''") + "'"
            now = "'" + str(update["set"]).replace("'", "''") + "'"
            undo.append(f"UPDATE Audiobooks SET BasePath = {was} WHERE Id = {book_id} AND BasePath = {now};")
        if refused or dry_run:
            connection.execute("ROLLBACK")
        else:
            # The undo goes to disk before the commit: a write that fails after it would leave no way back.
            stamp = datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ")
            (out_dir / f"undo-basepath-{stamp}.sql").write_text(
                "-- Reverses restore-basepath. Service STOPPED.\nBEGIN IMMEDIATE;\n" + "\n".join(undo)
                + "\nCOMMIT;\n", encoding="utf-8")
            connection.execute("COMMIT")
    except BaseException:
        if connection.in_transaction:
            connection.execute("ROLLBACK")
        raise
    finally:
        connection.close()
    if refused:
        raise BasePathRefused("; ".join(refused))
    return [int(b["audiobook_id"]) for b, _ in updates]


class MoveRefused(Exception):
    """A precondition failed before anything was moved."""


class MoveStopped(Exception):
    """The move stopped part-way: some files may have moved; the journal says which."""


def _check_move(entry: dict[str, Any], target_host: str, roots_host: list[str],
                restore_dirs: list[str]) -> str | None:
    source, destination, pin = str(entry["source"]), str(entry["destination"]), entry["pin"]
    if any(ch in source + destination for ch in "\t\n"):
        return "a name holds a tab or a newline (the journal could not record it)"
    try:
        (source + destination).encode("utf-8")
    except UnicodeEncodeError:
        return "a name is not valid UTF-8 (the journal could not record it)"
    info = lstat_or_none(source)
    if info is None or not stat.S_ISREG(info.st_mode):
        return "source is gone or no longer a regular file"
    if (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns, info.st_nlink) != (
            pin["dev"], pin["ino"], pin["size"], pin["mtime_ns"], pin["ctime_ns"], pin["nlink"]):
        return "source changed since the plan (device/inode/size/mtime/ctime/link count)"
    if not any(is_within(source, d) for d in restore_dirs) or any(is_within(source, r) for r in roots_host):
        return "source is not under a --restore-from folder, or lies under a root"
    if not is_within(destination, target_host) or not any(is_within(destination, r) for r in roots_host):
        return "destination is not inside the book's target folder under a root"
    if os.path.lexists(destination):
        return "destination name exists"
    target = lstat_or_none(target_host)
    if target is None or not stat.S_ISDIR(target.st_mode):
        return "target folder is gone or no longer a folder"
    if target.st_dev != info.st_dev:
        return "target folder is on another device (a move would be a copy)"
    return None


def _audio_under(folder: str) -> list[str]:
    """Relative names of the audio files under ``folder`` (not following links), sorted; [] when it is gone."""
    found: list[str] = []
    for directory, _, filenames in os.walk(folder, followlinks=False):
        found += [os.path.relpath(os.path.join(directory, n), folder) for n in filenames if is_audio(n)]
    return sorted(found)


def _fsync_dir(path: str) -> None:
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


class Journal:
    """Append-only record of every filesystem step, written and synced before and after each one."""

    def __init__(self, path: str) -> None:
        """Create the journal (it must not exist yet), and make its name durable."""
        self._fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_APPEND, 0o600)
        _fsync_dir(os.path.dirname(os.path.abspath(path)))

    def write(self, *cells: object) -> None:
        """One tab-separated line, synced to disk before returning."""
        os.write(self._fd, ("\t".join(str(c) for c in cells) + "\n").encode("utf-8"))
        os.fsync(self._fd)

    def close(self) -> None:
        """Close the journal file."""
        os.close(self._fd)


def move_plan(plan: dict[str, Any], roots_host: list[str], journal_path: str | None, dry_run: bool) -> Counter[str]:
    """Check every entry, then (not dry run) move each file back: link to the destination, then unlink the source.

    Nothing moves unless every entry passes. A folder the destination needs below the target is created (and
    journalled). Raises MoveRefused before any change, and MoveStopped (journal to undo from) part-way.
    """
    if plan.get("tool") != TOOL_NAME or plan.get("kind") != "quarantine-restore" or (
            plan.get("format") != RESTORE_PLAN_FORMAT):
        raise MoveRefused("not a quarantine-restore plan of this format")
    restore_dirs = [str(d) for d in plan["restore_dirs"]]
    refusals: Counter[str] = Counter()
    seen: set[str] = set()
    for book in plan["books"]:
        if _audio_under(str(book["target_host"])) != list(book["target_audio"]):
            refusals["target folder's audio changed since the plan"] += 1
        for entry in book["files"]:
            reason = _check_move(entry, str(book["target_host"]), roots_host, restore_dirs)
            # Casefolded: on a case-insensitive dataset two such names are one, and the second link would fail.
            if reason is None and str(entry["destination"]).casefold() in seen:
                reason = "two entries move to one destination"
            seen.add(str(entry["destination"]).casefold())
            if reason is not None:
                refusals[reason] += 1
    if refusals:
        raise MoveRefused("; ".join(f"{reason}: {count}" for reason, count in sorted(refusals.items())))
    done: Counter[str] = Counter(checked=sum(len(b["files"]) for b in plan["books"]))
    if dry_run:
        return done
    if journal_path is None:
        raise MoveRefused("a move needs a journal path")
    journal = Journal(journal_path)
    try:
        for book in plan["books"]:
            for entry in book["files"]:
                _move_one(entry, str(book["target_host"]), journal, done)
    except Exception as error:  # anything after the first journal line: undo from the journal, never a traceback
        raise MoveStopped(f"{type(error).__name__} after {done['moved']} file(s) moved") from error
    finally:
        journal.close()
    return done


def _move_one(entry: dict[str, Any], target_host: str, journal: Journal, done: Counter[str]) -> None:
    source, destination, pin = str(entry["source"]), str(entry["destination"]), entry["pin"]
    parent = os.path.dirname(destination)
    missing: list[str] = []
    while not os.path.lexists(parent) and is_within(parent, target_host):
        missing.insert(0, parent)
        parent = os.path.dirname(parent)
    for folder in missing:
        journal.write("mkdir", folder)
        os.mkdir(folder, 0o775)
        _fsync_dir(os.path.dirname(folder))
        done["folders_created"] += 1
    journal.write("intent", pin["dev"], pin["ino"], source, destination)
    os.link(source, destination, follow_symlinks=False)
    _fsync_dir(os.path.dirname(destination))
    moved = os.lstat(destination)
    still = os.lstat(source)
    if (moved.st_dev, moved.st_ino) != (pin["dev"], pin["ino"]) or (still.st_dev, still.st_ino) != (
            pin["dev"], pin["ino"]):
        raise OSError(f"object changed while being moved: {source}")
    os.unlink(source)
    _fsync_dir(os.path.dirname(source))
    journal.write("done", pin["dev"], pin["ino"], source, destination)
    done["moved"] += 1


def undo_journal(journal_path: str, undo_path: str | None, dry_run: bool) -> Counter[str]:
    """Reverse a move journal, newest step first. A step whose names no longer fit is left and counted."""
    with open(journal_path, encoding="utf-8") as handle:
        steps = [line.rstrip("\n").split("\t") for line in handle if line.strip()]
    counts: Counter[str] = Counter()
    undo = Journal(undo_path) if undo_path and not dry_run else None
    try:
        moves = {(s[3], s[4]): s for s in steps if s[0] in ("intent", "done")}
        for (source, destination), step in reversed(list(moves.items())):
            key = (int(step[1]), int(step[2]))
            at_source, at_destination = lstat_or_none(source), lstat_or_none(destination)
            source_is = at_source is not None and (at_source.st_dev, at_source.st_ino) == key
            destination_is = at_destination is not None and (at_destination.st_dev, at_destination.st_ino) == key
            if source_is and destination_is:
                action = "unlink-destination"  # interrupted between link and unlink
            elif destination_is and at_source is None and os.path.isdir(os.path.dirname(source)):
                action = "move-back"
            elif source_is and at_destination is None:
                action = "nothing-to-undo"
            else:
                counts["refused"] += 1
                continue
            counts[action] += 1
            if dry_run or action == "nothing-to-undo" or undo is None:
                continue
            undo.write(action, key[0], key[1], source, destination)
            if action == "move-back":
                os.link(destination, source, follow_symlinks=False)
                _fsync_dir(os.path.dirname(source))
            os.unlink(destination)
            _fsync_dir(os.path.dirname(destination))
        for step in reversed([s for s in steps if s[0] == "mkdir"]):
            folder = step[1]
            if os.path.isdir(folder) and not os.listdir(folder):
                counts["rmdir"] += 1
                if not dry_run and undo is not None:
                    undo.write("rmdir", folder)
                    os.rmdir(folder)
            elif os.path.lexists(folder):
                counts["folder-kept"] += 1
    finally:
        if undo is not None:
            undo.close()
    return counts


def write_restore_plan(plan: dict[str, Any], out_dir: str) -> tuple[str, str]:
    """Write restore-plan.json; return its path and sha256."""
    path = Path(out_dir) / "restore-plan.json"
    path.write_text(json.dumps(plan, indent=1) + "\n", encoding="utf-8")
    return str(path), file_sha256(path)
