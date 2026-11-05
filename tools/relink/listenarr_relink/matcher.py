"""Decide, per stale AudiobookFiles row, which file on disk holds its content - or leave it out.

Rules (each one exists because guessing here can later let a delete hit the wrong file):

* Size is the content anchor. A row without a usable stored size is only linked by physical identity.
* A stored size of 64 next to a stored duration of at least a second is .620's placeholder, not a size (see
  ``size_is_placeholder``). Physical identity then decides alone, without the size-changed veto; the
  candidate must still agree with the stored duration, codec, sample rate and channels.
* A candidate must agree with the row's stored duration, codec, sample rate and channels.
* Physical identity (inode + file handle/generation, on the stored device or, when the device minor drifted,
  on the library root's own dataset) decides on its own when it matches; ``physical.classify`` has the
  rules. A shared identity (two rows, or two files), a hard-linked object, a match on another dataset or pool,
  or a folder whose bracketed id is another book's leaves the row out. Under a drifted device the stored
  identity is cleared on APPLY (it no longer spells the file's device), as for a shape link.
* One exception to the hard-link refusal: a file whose every other name lies under a ``--quarantine-dir``
  folder, outside every root, on the file's own dataset, in a folder whose bracketed id is this book's id
  (``_other_names_quarantined``). The name under the root is the one linked.
* Otherwise exactly one consistent candidate must remain. Equal-size siblings are separated only by an
  unchanged file name; part numbers and track tags are never trusted (a renamer may have rewritten both in
  any order). A same-size file that cannot be read makes the row ambiguous (it might be this row's file).
  Anything else is ambiguous and left out. Such links are "shape-and-name": no content hash was ever
  stored, and adversarial review forced wrong ones repeatedly, so PLAN proposes them only with
  ``--include-shape-matches``. By default a book is proposed only when every one of its links is by
  physical identity.
* A link also needs path evidence (same folder as the old path, or a folder/file name or album/title tag
  that names exactly this title, see ``paths.names_title``). Title evidence alone never selects a file; it
  only confirms one. A title with no distinctive token (``1``, ``It``) is never evidence or a veto key.
* Tags that fully name another library book reject a candidate. A title that is part of this one does not
  when the tags also carry this book's whole title. For the object .620 registered (physical identity),
  neither does one whose remaining words are this book's own authors or series, again when the tags also
  carry this book's whole title (``other_book_named``). The veto is waived only for a physical identity match
  whose stored file handle (object + generation) and inode are the file's own and whose folder's bracketed
  id is this book's ``Asin`` (``_folder_is_asin``); every other check still applies.
* A file (one object, whatever its names) is never linked to two rows, never to a file a live row already
  holds (also through a symlink row), and never to a file that was also a candidate of a row left
  unresolved (it might be that row's file).
* A book with no live row is re-linked whole or not at all. Linking some of its rows while others stay
  stale would turn an all-stale book (round 27 refuses or skips its file deletes) into a mixed one (round
  27 recursively deletes its folder, FF report section 5).
* .620's own scan/move lists are empty when any folder could not be read: the service may see files PLAN
  could not.
* Rows the operator excludes (``--exclude-file-ids``) are never linked, and neither is any other row of a book
  holding one, live rows or not. They count as unresolved, so no other row takes a file they could own.
"""

from __future__ import annotations

import logging
import math
import os
import stat
from collections import Counter, defaultdict
from collections.abc import Callable, Iterable
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from functools import partial

from listenarr_relink import identity, media, physical
from listenarr_relink.inventory import BookRow, DiskFile, FileRow, Library, lstat_or_none
from listenarr_relink.paths import PathMap, is_distinctive, is_within, name_parts, names_title, resolve_service, tokens

log = logging.getLogger("relink.match")

# Listenarr's proc-fd lstat sentinel (#901): a stored size of 64 is not a real size.
UNUSABLE_SIZES = frozenset({0, 64})
# Before #901 was fixed, registration stored the length of the /proc/<pid>/fd/<n> link it read the file
# through (lstat on a proc magic link reports 64), whatever the file. A real 64-byte file cannot hold a
# second of audio (512 bits), so 64 beside a stored duration of a second or more is that placeholder.
PLACEHOLDER_SIZE = 64
PLACEHOLDER_MIN_DURATION_SECONDS = 1.0
EXCLUDED = "withheld: listed in --exclude-file-ids"
DURATION_ABS_TOLERANCE = 1.0
SHAPE = "shape-and-name"
TAGS_VETO = "tags name another library book"
DURATION_REL_TOLERANCE = 0.005


@dataclass
class Candidate:
    """A disk file considered for a row, with the evidence gathered about it."""

    file: DiskFile
    probe: media.Probe | None = None
    rejected: str | None = None
    same_dir: bool = False
    title_in_path: bool = False
    title_in_tags: bool = False
    author_in_path: bool = False
    identity_match: bool = False
    device_drift: bool = False
    handle_equal: bool = False
    hardlinked_to_quarantine: bool = False
    tags_waived: bool = False  # tags name another book, waived: the handle and the folder's Asin agree

    @property
    def path_evidence(self) -> bool:
        """Same folder, or the title in the path or the tags."""
        return self.same_dir or self.title_in_path or self.title_in_tags


@dataclass
class RowDecision:
    """Outcome for one stale row."""

    row: FileRow
    candidates: list[Candidate] = field(default_factory=list)
    chosen: Candidate | None = None
    confidence: str | None = None
    via: str | None = None
    reason: str | None = None
    audio_hashes: dict[str, str | None] = field(default_factory=dict)
    identity_class: str | None = None
    folder_id: str | None = None  # agrees, disagrees or unknown, for a physical identity match

    @property
    def consistent(self) -> list[Candidate]:
        """Candidates that survived the consistency checks."""
        return [c for c in self.candidates if c.rejected is None]

    def leave_out(self, reason: str) -> None:
        """Drop any choice and record why."""
        self.chosen, self.confidence, self.via, self.reason = None, None, None, reason


@dataclass
class Flag:
    """Something the operator should see that the tool deliberately does not change."""

    kind: str
    row_id: int | None
    audiobook_id: int | None
    detail: str


@dataclass
class BookUpdate:
    """Columns of one Audiobooks row to change, with the values they must still have."""

    book: BookRow
    set_values: dict[str, str]


@dataclass
class MatchResult:
    """Everything the plan is built from."""

    decisions: list[RowDecision]
    live_rows: int
    book_updates: list[BookUpdate]
    flags: list[Flag]
    scan_safe_books: list[int]
    move_safe_books: dict[int, str]
    orphans: list[DiskFile]
    keep_identity: dict[int, bool]


def size_is_placeholder(row: FileRow) -> bool:
    """True when the row's stored size is .620's proc-fd placeholder rather than a measurement."""
    duration = row.values.get("DurationSeconds")
    if row.size != PLACEHOLDER_SIZE or duration is None:
        return False
    seconds = float(str(duration))
    return math.isfinite(seconds) and seconds >= PLACEHOLDER_MIN_DURATION_SECONDS


def veto_keys(books: dict[int, BookRow]) -> dict[frozenset[str], int]:
    """Token set of every distinctive library title -> the first book holding it."""
    keys: dict[frozenset[str], int] = {}
    for book in books.values():
        if is_distinctive(book.title):
            keys.setdefault(frozenset(tokens(book.title)), book.id)
    return keys


def tag_tokens(tags: dict[str, str]) -> frozenset[str]:
    """Tokens of the album and title tags, the two that name a work."""
    return frozenset(tokens(" ".join(tags.get(k, "") for k in ("album", "title"))))


def own_title_tagged(tagged: frozenset[str], own: BookRow | None) -> bool:
    """True when the tags carry every token of this book's (distinctive) title."""
    return own is not None and is_distinctive(own.title) and frozenset(tokens(own.title)) <= tagged


def explained_by_own_book(title_tokens: frozenset[str], own: BookRow | None) -> bool:
    """True when every word of another title that is not in this book's title is in its authors or series."""
    if own is None:
        return False
    own_words = {t for text in own.own_text for t in tokens(text)}
    return title_tokens - frozenset(tokens(own.title)) <= own_words


def other_book_named(tags: dict[str, str], other_titles: dict[frozenset[str], int], own: BookRow | None,
                     identity_match: bool) -> int | None:
    """The library book other than ``own`` whose title the tags fully name, or None.

    A title that is part of this book's own title (``Pellucidar`` in ``Tanar of Pellucidar``) counts only when
    the tags lack the rest of this title: that book's own file, rewritten into this object, carries its title
    and not the words this one adds. For a file whose physical identity matched, a title explained by this
    book's own text (``Allan Quatermain``, the series, in tags of ``Marie``) does not count either while the
    tags also carry this book's whole title. A different book's real tags rarely carry both, and
    the identity match already rules out a renamer's swap of two files: only content rewritten into this
    object is left to catch. Without identity the tags may be all that separates two books, so they keep the
    strict reading.

    Accepted on purpose: another edition of the same work whose title is this book's series (``Peter Pan``
    for ``Peter and Wendy``), rewritten into this object with tags that also carry this title, is not
    vetoed. Tags cannot tell it from this book's own series text; the stored duration, codec, sample rate
    and channels are then the only check on the content.
    """
    tagged = tag_tokens(tags)
    own_tokens = frozenset(tokens(own.title)) if own else frozenset()
    own_tagged = own_title_tagged(tagged, own)
    for title_tokens, book_id in other_titles.items():
        if not title_tokens <= tagged:
            continue
        if title_tokens <= own_tokens:
            if own_tagged:
                continue
            return book_id
        if identity_match and own_tagged and explained_by_own_book(title_tokens, own):
            continue
        return book_id
    return None


def _consistency(row: FileRow, probe: media.Probe, other_titles: dict[frozenset[str], int],
                 own: BookRow | None, identity_match: bool) -> str | None:
    if probe.error:
        return f"not readable as audio: {probe.error}"
    stored_duration = row.values.get("DurationSeconds")
    if stored_duration is not None and not math.isfinite(float(str(stored_duration))):
        return f"stored duration {stored_duration} is not a finite number"
    if stored_duration is not None and probe.duration is not None:
        expected = float(str(stored_duration))
        tolerance = max(DURATION_ABS_TOLERANCE, DURATION_REL_TOLERANCE * expected)
        if abs(expected - probe.duration) > tolerance:
            return f"duration {probe.duration:.2f}s != stored {expected:.2f}s"
    stored_codec = "".join(tokens(str(row.values.get("Codec") or "")))
    found_codec = "".join(tokens(probe.codec or ""))
    if stored_codec and found_codec and not (stored_codec.startswith(found_codec)
                                             or found_codec.startswith(stored_codec)):
        return f"codec {probe.codec} != stored {row.values.get('Codec')}"
    for column, value in (("SampleRate", probe.sample_rate), ("Channels", probe.channels)):
        stored = row.values.get(column)
        if stored is not None and value is not None and int(str(stored)) != value:
            return f"{column} {value} != stored {stored}"
    book_id = other_book_named(probe.tags, other_titles, own, identity_match)
    return None if book_id is None else f"{TAGS_VETO} (audiobook {book_id})"


def _own_book_id(row: FileRow, library: Library) -> str | None:
    """The book's id: its ``Asin`` when the database has one, else the id ending a folder of the stored path."""
    book = library.books.get(row.audiobook_id)
    return (book.context.get("Asin", "").strip().casefold() if book else "") or physical.folder_id(
        row.path, root_of(row.path, library.roots))


def _folder_id_check(row: FileRow, hit: DiskFile, library: Library) -> str:
    """Whether the matched file's folder carries this book's bracketed id: agrees, disagrees or unknown."""
    own = _own_book_id(row, library)
    found = physical.folder_id(hit.db_path, root_of(hit.db_path, library.roots))
    if not own or not found:
        return "unknown"
    return "agrees" if own == found else "disagrees"


def _folder_is_asin(disk_file: DiskFile, book: BookRow | None, library: Library) -> bool:
    """True when the file's folder carries a bracketed id equal to the book's own ``Asin`` (none: False)."""
    asin = book.context.get("Asin", "").strip().casefold() if book else ""
    return bool(asin) and physical.folder_id(disk_file.db_path, root_of(disk_file.db_path, library.roots)) == asin


def _other_names_quarantined(own_id: str | None, library: Library, host_roots: frozenset[str], hit: DiskFile) -> bool:
    """True when every other name of ``hit`` is a quarantine name of this book.

    Each must lie under a ``--quarantine-dir`` folder, outside every library root, on the file's own device,
    in a folder whose bracketed id is this book's id; and the quarantine names must account for every link.
    A same-inode file of another device counts against the total, so it refuses rather than stands in.
    """
    names = library.quarantine.get(hit.ino, [])
    return bool(own_id) and len(names) == hit.nlink - 1 and all(
        name.dev == hit.dev
        and not any(is_within(name.host_path, root) for root in host_roots)
        and physical.folder_id(name.host_path, name.quarantine_dir) == own_id
        for name in names)


def _resolves(db_path: str, path_map: PathMap) -> bool:
    """True when the stored path reaches a regular file as the service sees it."""
    try:
        resolved = resolve_service(db_path, path_map)
        return resolved is not None and stat.S_ISREG(os.stat(resolved).st_mode)
    except OSError:
        return False


def root_of(path: str, roots: list[str]) -> str | None:
    """The longest root ``path`` lies in, or None."""
    matching = [root for root in roots if is_within(path, root)]
    return max(matching, key=len) if matching else None


def _parallel(items: Iterable[str], work: Callable[[str], object], workers: int) -> dict[str, object]:
    unique = sorted(set(items))
    with ThreadPoolExecutor(max_workers=workers) as pool:
        return dict(zip(unique, pool.map(work, unique), strict=True))


def match(library: Library, path_map: PathMap, workers: int, hash_chosen: bool, identity_only: bool,
          excluded: frozenset[int] = frozenset()) -> MatchResult:
    """Classify rows, choose links, derive book updates and the scan-safe list; never link an excluded row."""
    flags: list[Flag] = []
    files_by_book: dict[int, list[FileRow]] = defaultdict(list)
    for row in library.files:
        files_by_book[row.audiobook_id].append(row)

    stale: list[FileRow] = []
    claimed: set[tuple[int, int]] = set()
    symlink_books: set[int] = set()
    occupied: dict[int, int] = {}  # book -> a row whose stored path holds something other than its file
    row_paths = {row.path for row in library.files if row.path}
    live_rows = 0
    for row in library.files:
        if not row.path:
            flags.append(Flag("empty-path", row.id, row.audiobook_id, "row has no stored path"))
            continue
        host = path_map.to_host(row.path)
        try:
            resolved = resolve_service(row.path, path_map)
            info = os.stat(resolved) if resolved is not None else None
        except OSError as error:
            flags.append(Flag("unreadable", row.id, row.audiobook_id, f"{row.path}: {error.strerror}"))
            library.walk_errors.append(f"{row.path}: cannot resolve the row's path: {error.strerror}")
            continue
        if info is None or resolved is None:
            if lstat_or_none(host) is None:
                stale.append(row)
                continue
            occupied[row.audiobook_id] = row.id  # a dangling symlink sits at the stored path
            flags.append(Flag("path-occupied", row.id, row.audiobook_id, f"{row.path} is a dangling symlink"))
            continue
        if not stat.S_ISREG(info.st_mode):
            # Something that is not a file sits at the stored path: no file is held, but APPLY could not
            # re-link the row either ("old path exists again"). Left out; it counts as unresolved for its book.
            occupied[row.audiobook_id] = row.id
            flags.append(Flag("path-occupied", row.id, row.audiobook_id, f"{row.path} is not a regular file"))
            continue
        if resolved != host:
            # Reached through a symlink (the file itself or a folder above it): the object is held all the same.
            flags.append(Flag("live-symlink", row.id, row.audiobook_id, f"{row.path} -> {resolved}"))
            symlink_books.add(row.audiobook_id)
            claimed.add((info.st_dev, info.st_ino))
            continue
        live_rows += 1
        claimed.add((info.st_dev, info.st_ino))
        if row.size is not None and row.size not in UNUSABLE_SIZES and row.size != info.st_size:
            flags.append(Flag("live-size-mismatch", row.id, row.audiobook_id,
                              f"{row.path}: stored {row.size}, on disk {info.st_size}"))
        stored_identity = identity.parse_stored(str(row.values.get("PhysicalObjectIdentity") or ""))
        if stored_identity is not None:
            live = identity.read_live(host)
            if live is not None and not live.matches(stored_identity):
                # Same inode and handle on another device minor is reported apart: it may be a remount, or a clone.
                kind = "live-identity-device-drift" if live.same_object(stored_identity) else "live-identity-mismatch"
                flags.append(Flag(kind, row.id, row.audiobook_id,
                                  f"{row.path}: the file at this path does not carry the row's stored identity"))

    pool = [f for f in library.disk if (f.dev, f.ino) not in claimed and f.db_path not in row_paths]
    by_size: dict[int, list[DiskFile]] = defaultdict(list)
    by_inode: dict[int, list[DiskFile]] = defaultdict(list)
    for disk_file in pool:
        by_size[disk_file.size].append(disk_file)
        by_inode[disk_file.ino].append(disk_file)
    log.info("rows=%d live=%d stale=%d unclaimed_audio_files=%d", len(library.files), live_rows, len(stale), len(pool))

    other_titles = veto_keys(library.books)

    decisions = [RowDecision(row) for row in stale]
    keep_identity: dict[int, bool] = {}

    # Pass 1: physical identity decides on its own.
    stored_of = {row.id: identity.parse_stored(str(row.values.get("PhysicalObjectIdentity") or ""))
                 for row in library.files}
    stale_rows_by_inode = Counter(s.inode for d in decisions if (s := stored_of[d.row.id]) is not None)
    rows_by_handle = Counter((s.inode, s.handle) for s in stored_of.values() if s is not None and s.handle)
    devices_under_roots = frozenset({f.dev for f in library.disk} | set(library.root_devices.values()))

    def root_device_of(disk_file: DiskFile) -> int | None:
        root = root_of(disk_file.db_path, library.roots)
        return library.root_devices.get(root) if root is not None else None

    host_roots = frozenset(p for r in library.roots
                           for p in (path_map.to_host(r), os.path.realpath(path_map.to_host(r))))
    identity_taken: dict[str, RowDecision] = {}
    for decision in decisions:
        stored_identity = stored_of[decision.row.id]
        if stored_identity is None:
            decision.identity_class = physical.NO_STORED
            continue
        sharing = max(stale_rows_by_inode[stored_identity.inode],
                      rows_by_handle[(stored_identity.inode, stored_identity.handle)] if stored_identity.handle else 0)
        if sharing > 1:
            decision.identity_class = physical.AMBIGUOUS_ROWS
            decision.leave_out(f"physical identity (inode or file handle) is shared by {sharing} rows (ambiguous, "
                               "not linked)")
            # Its files stay its candidates: pass 3 then keeps them from any other row's size match.
            decision.candidates = [Candidate(f) for f in by_inode.get(stored_identity.inode, [])]
            continue
        verdict = physical.classify(stored_identity, by_inode.get(stored_identity.inode, []), identity.read_live,
                                    root_device_of, devices_under_roots, library.device_pools,
                                    partial(_other_names_quarantined, _own_book_id(decision.row, library), library,
                                            host_roots))
        decision.identity_class = verdict.kind
        if verdict.kind in physical.FALL_THROUGH:
            continue
        if verdict.kind != physical.MATCH:
            decision.leave_out(verdict.detail)
            decision.candidates = [Candidate(f, identity_match=verdict.kind == physical.HARDLINKED)
                                   for f in verdict.hits]
            continue
        (hit,) = verdict.hits
        decision.candidates = [Candidate(hit, identity_match=True, device_drift=verdict.device_drift,
                                         handle_equal=verdict.handle_equal,
                                         hardlinked_to_quarantine=verdict.hardlinked_to_quarantine)]
        decision.folder_id = _folder_id_check(decision.row, hit, library)
        if decision.folder_id == "disagrees":
            decision.identity_class = physical.FOLDER_ID_DISAGREES
            decision.leave_out(f"physical identity matches {hit.db_path} but its folder id is not this book's "
                               "(review)")
            flags.append(Flag("folder-id-disagrees", decision.row.id, decision.row.audiobook_id,
                              f"{decision.row.path} -> {hit.db_path}"))
            continue
        if decision.row.size != hit.size and not size_is_placeholder(decision.row):
            decision.leave_out(f"physical identity matches {hit.db_path} but its size changed "
                               f"({decision.row.size} -> {hit.size}); content was rewritten, review")
            continue
        if hit.db_path in identity_taken:
            other = identity_taken[hit.db_path]
            other.leave_out("two rows carry the same physical identity")
            decision.leave_out("two rows carry the same physical identity")
            continue
        identity_taken[hit.db_path] = decision

    # Gather size candidates for everyone not yet holding an identity hit, then probe in parallel.
    pending: list[RowDecision] = []
    for decision in decisions:
        if decision.reason is not None:
            continue
        if decision.candidates and decision.candidates[0].identity_match:
            pending.append(decision)
            continue
        size = decision.row.size
        if size is None or size in UNUSABLE_SIZES:
            decision.leave_out("no usable stored size and no physical identity match")
            continue
        decision.candidates = [Candidate(f) for f in by_size.get(size, []) if f.db_path not in identity_taken]
        pending.append(decision)

    probes = _parallel((c.file.host_path for d in pending for c in d.candidates), media.probe, workers)
    for decision in pending:
        owner = library.books.get(decision.row.audiobook_id)
        title = owner.title if owner else ""
        authors = owner.authors if owner else []
        old_parent = os.path.dirname(path_map.to_host(decision.row.path))
        for candidate in decision.candidates:
            probe = probes[candidate.file.host_path]
            assert isinstance(probe, media.Probe)
            candidate.probe = probe
            others = {k: v for k, v in other_titles.items() if v != decision.row.audiobook_id}
            candidate.rejected = _consistency(decision.row, probe, others, owner, candidate.identity_match)
            if (candidate.rejected is not None and candidate.rejected.startswith(TAGS_VETO)
                    and candidate.identity_match and candidate.handle_equal
                    and _folder_is_asin(candidate.file, owner, library)):
                candidate.rejected, candidate.tags_waived = None, True
            root = root_of(candidate.file.db_path, library.roots) or "/"
            parts = name_parts(candidate.file.db_path, root)
            candidate.same_dir = os.path.dirname(candidate.file.host_path) == old_parent
            candidate.title_in_path = any(names_title(part, title, authors) for part in parts)
            candidate.title_in_tags = any(names_title(probe.tags[k], title, authors)
                                          for k in ("album", "title") if probe.tags.get(k))
            author_tokens = [set(tokens(a)) for a in authors if tokens(a)]
            candidate.author_in_path = any(a <= {t for part in parts for t in tokens(part)} for a in author_tokens)

    # Pass 2: choose.
    for decision in pending:
        consistent = decision.consistent
        if decision.candidates and decision.candidates[0].identity_match:
            if not consistent:
                decision.leave_out(f"physical identity matches but {decision.candidates[0].rejected}")
                continue
            placeholder = size_is_placeholder(decision.row)
            if placeholder and (consistent[0].probe is None or consistent[0].probe.duration is None):
                decision.leave_out("physical identity matches but the stored size is the placeholder and the "
                                   "file's duration, which stands in for it, could not be read")
                continue
            via = "physical identity" + (" (stored size is the 64-byte placeholder)" if placeholder else "")
            decision.chosen, decision.confidence, decision.via = consistent[0], "identity", via
            continue
        unreadable = [c for c in decision.candidates if c.probe is not None and c.probe.error]
        if unreadable:
            decision.leave_out(f"same-size candidate {unreadable[0].file.db_path} could not be read "
                               f"({unreadable[0].probe.error if unreadable[0].probe else ''}); it might be this "
                               "row's file (review)")
            continue
        if not consistent:
            decision.leave_out("no same-size file agrees with the stored audio facts" if decision.candidates
                               else "no unclaimed file of the stored size")
            continue
        if len(consistent) == 1:
            chosen, via = consistent[0], "only consistent candidate"
        else:
            hashes = _parallel((c.file.host_path for c in consistent), media.audio_hash, workers)
            decision.audio_hashes = {k: v if isinstance(v, str) else None for k, v in hashes.items()}
            old_name = os.path.basename(decision.row.path)
            by_name = [c for c in consistent if os.path.basename(c.file.db_path) == old_name]
            if len(by_name) != 1:
                decision.leave_out(f"{len(consistent)} equally consistent candidates; only an unchanged file name "
                                   f"may separate equal-size files and {len(by_name)} carry it")
                continue
            chosen, via = by_name[0], "unchanged file name among equal-size files"
        if not chosen.path_evidence:
            decision.leave_out(f"only candidate {chosen.file.db_path} has no folder/title/tag evidence "
                               "linking it to this book (review)")
            decision.candidates = [chosen, *[c for c in decision.candidates if c is not chosen]]
            continue
        decision.chosen, decision.confidence, decision.via = chosen, SHAPE, via

    # Excluded rows are unresolved from here on: their candidates stay theirs (pass 3) and their book whole.
    for decision in decisions:
        if decision.row.id in excluded:
            decision.leave_out(EXCLUDED + (f" (otherwise left out: {decision.reason})" if decision.reason else ""))

    # Pass 3: one file, one row; and a file that an unresolved row could own is not given away.
    while True:
        changed = False
        chosen_by: dict[tuple[int, int], list[RowDecision]] = defaultdict(list)
        for decision in decisions:
            if decision.chosen is not None:
                chosen_by[(decision.chosen.file.dev, decision.chosen.file.ino)].append(decision)
        for owners in chosen_by.values():
            if len(owners) > 1:
                object_names = sorted({o.chosen.file.db_path for o in owners if o.chosen})
                for contender in owners:
                    contender.leave_out(f"{', '.join(object_names)} (one file object) was chosen by {len(owners)} rows")
                changed = True
        for decision in decisions:
            if decision.chosen is None or decision.confidence == "identity":
                continue
            path = decision.chosen.file.db_path
            obj = (decision.chosen.file.dev, decision.chosen.file.ino)
            contenders = [d for d in decisions if d is not decision
                          and d.confidence != "identity"
                          and any((c.file.dev, c.file.ino) == obj and c.rejected is None for c in d.candidates)
                          and (d.chosen is None or (d.chosen.file.dev, d.chosen.file.ino) == obj)]
            if contenders:
                decision.leave_out(f"{path} is also a consistent candidate of unresolved row(s) "
                                   f"{', '.join(str(d.row.id) for d in contenders)}")
                changed = True
        if not changed:
            break

    if identity_only:
        shape_books = {d.row.audiobook_id for d in decisions if d.chosen and d.confidence != "identity"}
        for decision in decisions:
            if decision.chosen is not None and decision.row.audiobook_id in shape_books:
                decision.leave_out("withheld (identity-only, the default): this book has a link without "
                                   "physical identity")
    stale_ids = {d.row.id for d in decisions}
    books_with_live_row = {r.audiobook_id for r in library.files if r.id not in stale_ids
                           and r.path and _resolves(r.path, path_map)}
    unresolved = {d.row.audiobook_id: d.row.id for d in decisions if d.chosen is None}
    for row in library.files:
        if not row.path:
            unresolved.setdefault(row.audiobook_id, row.id)  # an empty Path is not a live file either
    for book_id, row_id in occupied.items():
        unresolved.setdefault(book_id, row_id)
    for decision in decisions:
        book_id = decision.row.audiobook_id
        if decision.chosen is not None and book_id in unresolved and book_id not in books_with_live_row:
            decision.leave_out(f"withheld so the book is not left partly linked: its row {unresolved[book_id]} "
                               "stays stale (an all-stale book is safer under round 27 than a mixed one)")
    excluded_in_book = {row.audiobook_id: row.id for row in library.files if row.id in excluded}
    for decision in decisions:
        book_id = decision.row.audiobook_id
        if decision.chosen is not None and book_id in excluded_in_book:
            decision.leave_out(f"withheld: its book holds row {excluded_in_book[book_id]}, which is listed in "
                               "--exclude-file-ids")

    for decision in decisions:
        if decision.chosen is None:
            continue
        stored_identity = identity.parse_stored(str(decision.row.values.get("PhysicalObjectIdentity") or ""))
        keep_identity[decision.row.id] = (decision.confidence == "identity" and stored_identity is not None
                                          and not decision.chosen.device_drift)
        if hash_chosen and decision.chosen.file.host_path not in decision.audio_hashes:
            decision.audio_hashes[decision.chosen.file.host_path] = media.audio_hash(decision.chosen.file.host_path)

    book_updates = _book_updates(library, path_map, decisions, files_by_book, flags)
    if library.walk_errors:
        flags.append(Flag("walk-errors", None, None, f"{len(library.walk_errors)} folder(s)/file(s) could not be "
                          "read; .620's own scan/move lists are withheld and the plan may miss candidates"))
        scan_safe: list[int] = []
        move_safe: dict[int, str] = {}
    else:
        scan_safe = [b for b in _scan_safe_books(library, path_map, decisions, files_by_book, stale)
                     if b not in symlink_books]
        move_safe = {b: p for b, p in _move_safe_books(library, decisions, files_by_book, book_updates).items()
                     if b not in symlink_books}
    chosen_paths = {d.chosen.file.db_path for d in decisions if d.chosen}
    candidate_paths = {c.file.db_path for d in decisions for c in d.candidates}
    orphans = [f for f in pool if f.db_path not in chosen_paths and f.db_path not in candidate_paths]
    return MatchResult(decisions, live_rows, book_updates, flags, scan_safe, move_safe, orphans, keep_identity)


def _final_paths(rows: list[FileRow], links: dict[int, str], stale_ids: set[int]) -> list[str] | None:
    final: list[str] = []
    for row in rows:
        if row.id in links:
            final.append(links[row.id])
        elif row.id in stale_ids:
            return None
        elif row.path:
            final.append(row.path)
    return final


def _book_updates(library: Library, path_map: PathMap, decisions: list[RowDecision],
                  files_by_book: dict[int, list[FileRow]], flags: list[Flag]) -> list[BookUpdate]:
    links = {d.row.id: d.chosen.file.db_path for d in decisions if d.chosen}
    old_by_row = {d.row.id: d.row.path for d in decisions if d.chosen}
    stale_ids = {d.row.id for d in decisions}
    touched = {d.row.audiobook_id for d in decisions if d.chosen}
    every_final: dict[int, list[str]] = {}
    for book_id, rows in files_by_book.items():
        final = _final_paths(rows, links, stale_ids)
        every_final[book_id] = final if final is not None else [links.get(r.id, r.path) for r in rows if r.path]
    updates: list[BookUpdate] = []
    for book_id in sorted(touched):
        book = library.books.get(book_id)
        if book is None:
            continue
        set_values: dict[str, str] = {}
        for row in files_by_book[book_id]:
            if row.id in links and book.file_path and book.file_path == old_by_row[row.id]:
                set_values["FilePath"] = links[row.id]
        base = book.base_path
        if base and not os.path.isdir(path_map.to_host(base)):
            final = _final_paths(files_by_book[book_id], links, stale_ids)
            new_base = os.path.commonpath([os.path.dirname(p) for p in final]) if final else None
            root = root_of(new_base, library.roots) if new_base else None
            reason = None
            if final is None:
                reason = "some of the book's rows stay unresolved"
            elif new_base is None or root is None or new_base.rstrip("/") == root.rstrip("/"):
                reason = "the re-linked files do not share a folder below a root"
            else:
                for other_id, other_final in every_final.items():
                    other = library.books.get(other_id)
                    if other_id == book_id or other is None:
                        continue
                    if any(is_within(p, new_base) for p in other_final) or (
                            other.base_path and is_within(other.base_path, new_base)):
                        reason = f"the new folder {new_base} also holds audiobook {other_id}"
                        break
            if reason is None and new_base is not None:
                own = set(final or [])
                foreign = [f.db_path for f in library.disk if is_within(f.db_path, new_base) and f.db_path not in own]
                if foreign:
                    reason = f"the new folder {new_base} also holds {len(foreign)} audio file(s) not of this book"
            if reason is None and new_base is not None:
                set_values["BasePath"] = new_base
            else:
                flags.append(Flag("basepath-left-stale", None, book_id, f"{base}: {reason}"))
        elif base:
            outside = [links[r.id] for r in files_by_book[book_id]
                       if r.id in links and not is_within(links[r.id], base)]
            if outside:
                flags.append(Flag("relinked-outside-basepath", None, book_id,
                                  f"{len(outside)} re-linked file(s) lie outside BasePath {base}"))
        if set_values:
            updates.append(BookUpdate(book, set_values))
    return updates


def _scan_safe_books(library: Library, path_map: PathMap, decisions: list[RowDecision],
                     files_by_book: dict[int, list[FileRow]], stale: list[FileRow]) -> list[int]:
    """Books whose own per-book scan in .620 can only claim exactly the files the plan expects."""
    stale_by_book: dict[int, list[RowDecision]] = defaultdict(list)
    for decision in decisions:
        stale_by_book[decision.row.audiobook_id].append(decision)
    targets_by_book: dict[int, set[str]] = defaultdict(set)
    for decision in decisions:
        if decision.chosen:
            targets_by_book[decision.row.audiobook_id].add(decision.chosen.file.db_path)
    safe: list[int] = []
    for book_id, book_decisions in sorted(stale_by_book.items()):
        book = library.books.get(book_id)
        if book is None or not book.base_path or not os.path.isdir(path_map.to_host(book.base_path)):
            continue
        base = book.base_path
        if any(d.chosen is None or not is_within(d.chosen.file.db_path, base) for d in book_decisions):
            continue
        own = {r.path for r in files_by_book[book_id]} | targets_by_book[book_id]
        audio_inside = {f.db_path for f in library.disk if is_within(f.db_path, base)}
        if not audio_inside <= own:
            continue
        if any(is_within(link, base) for link in library.symlinks):
            continue
        if any(other_id != book_id and ((other.base_path and is_within(other.base_path, base))
                                         or any(is_within(r.path, base) for r in files_by_book.get(other_id, []))
                                         or any(is_within(t, base) for t in targets_by_book.get(other_id, set())))
               for other_id, other in library.books.items()):
            continue
        safe.append(book_id)
    return safe


def _move_safe_books(library: Library, decisions: list[RowDecision], files_by_book: dict[int, list[FileRow]],
                     book_updates: list[BookUpdate]) -> dict[int, str]:
    """Books .620's metadata-only move (``moveFiles: false``) re-points exactly as planned.

    That route swaps the old BasePath prefix for the new one on every row, so it is right only when every
    row of the book was stale, is planned, and its new path is the new folder plus the old relative path.
    """
    links = {d.row.id: d.chosen.file.db_path for d in decisions if d.chosen}
    safe: dict[int, str] = {}
    for update in book_updates:
        new_base = update.set_values.get("BasePath")
        old_base = update.book.base_path
        rows = files_by_book.get(update.book.id, [])
        if not new_base or not old_base or not rows:
            continue
        if all(r.id in links and is_within(r.path, old_base) and r.path != old_base
               and links[r.id] == new_base.rstrip("/") + r.path[len(old_base.rstrip("/")):] for r in rows):
            safe[update.book.id] = new_base
    return safe
