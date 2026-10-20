"""Decide, per stale AudiobookFiles row, which file on disk holds its content - or leave it out.

Rules (each one exists because guessing here can later let a delete hit the wrong file):

* Size is the content anchor. A row without a usable stored size is only linked by physical identity.
* A candidate must agree with the row's stored duration, codec, sample rate and channels.
* Physical identity (device + inode + file handle/generation) decides on its own when it matches.
* Otherwise exactly one consistent candidate must remain; equal-size siblings are separated only by
  file-name part number AND track tag agreeing. Anything else is ambiguous and left out.
* A link also needs path evidence (same folder as the old path, or the book title in the folder/file
  names or in the tags). Title evidence alone never selects a file; it only confirms one.
* A file is never linked to two rows, never to a file a live row already holds, and never to a file
  that was also a candidate of a row left unresolved (it might be that row's file).
"""

from __future__ import annotations

import logging
import os
import stat
from collections import defaultdict
from collections.abc import Callable, Iterable
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field

from listenarr_relink import identity, media
from listenarr_relink.inventory import BookRow, DiskFile, FileRow, Library, lstat_or_none
from listenarr_relink.paths import PathMap, coverage, is_within, part_number, path_tokens, tokens

log = logging.getLogger("relink.match")

# Listenarr's proc-fd lstat sentinel (#901): a stored size of 64 is not a real size.
UNUSABLE_SIZES = frozenset({0, 64})
TITLE_COVERAGE = 0.8
DURATION_ABS_TOLERANCE = 1.0
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


def _consistency(row: FileRow, probe: media.Probe, other_titles: dict[frozenset[str], int],
                 own_title: str) -> str | None:
    if probe.error:
        return f"not readable as audio: {probe.error}"
    stored_duration = row.values.get("DurationSeconds")
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
    tag_text = " ".join(probe.tags.get(k, "") for k in ("album", "title"))
    tag_tokens = frozenset(tokens(tag_text))
    if tag_tokens and coverage(own_title, set(tag_tokens)) < 0.5:
        for title_tokens, book_id in other_titles.items():
            if title_tokens and title_tokens <= tag_tokens:
                return f"tags name another library book (audiobook {book_id})"
    return None


def _root_of(path: str, roots: list[str]) -> str | None:
    matching = [root for root in roots if is_within(path, root)]
    return max(matching, key=len) if matching else None


def _parallel(items: Iterable[str], work: Callable[[str], object], workers: int) -> dict[str, object]:
    unique = sorted(set(items))
    with ThreadPoolExecutor(max_workers=workers) as pool:
        return dict(zip(unique, pool.map(work, unique), strict=True))


def match(library: Library, path_map: PathMap, workers: int, hash_chosen: bool) -> MatchResult:
    """Classify rows, choose links, derive book updates and the scan-safe list."""
    flags: list[Flag] = []
    files_by_book: dict[int, list[FileRow]] = defaultdict(list)
    for row in library.files:
        files_by_book[row.audiobook_id].append(row)

    stale: list[FileRow] = []
    claimed: set[tuple[int, int]] = set()
    row_paths = {row.path for row in library.files if row.path}
    live_rows = 0
    for row in library.files:
        if not row.path:
            flags.append(Flag("empty-path", row.id, row.audiobook_id, "row has no stored path"))
            continue
        host = path_map.to_host(row.path)
        try:
            info = lstat_or_none(host)
        except OSError as error:
            flags.append(Flag("unreadable", row.id, row.audiobook_id, f"{row.path}: {error.strerror}"))
            continue
        if info is None:
            stale.append(row)
            continue
        if stat.S_ISLNK(info.st_mode):
            flags.append(Flag("live-symlink", row.id, row.audiobook_id, row.path))
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
                flags.append(Flag("live-identity-mismatch", row.id, row.audiobook_id,
                                  f"{row.path}: the file at this path is not the object the row was registered with"))

    pool = [f for f in library.disk if (f.dev, f.ino) not in claimed and f.db_path not in row_paths]
    by_size: dict[int, list[DiskFile]] = defaultdict(list)
    by_inode: dict[tuple[int, int, int], list[DiskFile]] = defaultdict(list)
    for disk_file in pool:
        by_size[disk_file.size].append(disk_file)
        by_inode[(os.major(disk_file.dev), os.minor(disk_file.dev), disk_file.ino)].append(disk_file)
    log.info("rows=%d live=%d stale=%d unclaimed_audio_files=%d", len(library.files), live_rows, len(stale), len(pool))

    other_titles: dict[frozenset[str], int] = {}
    for book in library.books.values():
        key = frozenset(tokens(book.title))
        if key:
            other_titles.setdefault(key, book.id)

    decisions = [RowDecision(row) for row in stale]
    keep_identity: dict[int, bool] = {}

    # Pass 1: physical identity decides on its own.
    identity_taken: dict[str, RowDecision] = {}
    for decision in decisions:
        stored_identity = identity.parse_stored(str(decision.row.values.get("PhysicalObjectIdentity") or ""))
        if stored_identity is None:
            continue
        hits = [f for f in by_inode.get((stored_identity.major, stored_identity.minor, stored_identity.inode), [])
                if (live := identity.read_live(f.host_path)) is not None and live.matches(stored_identity)]
        if len(hits) > 1:
            decision.leave_out(f"physical identity matches {len(hits)} names of one object (hard links)")
            decision.candidates = [Candidate(f, identity_match=True) for f in hits]
            continue
        if len(hits) == 1:
            decision.candidates = [Candidate(hits[0], identity_match=True)]
            if decision.row.size != hits[0].size:
                decision.leave_out(f"physical identity matches {hits[0].db_path} but its size changed "
                                   f"({decision.row.size} -> {hits[0].size}); content was rewritten, review")
                continue
            if hits[0].db_path in identity_taken:
                other = identity_taken[hits[0].db_path]
                other.leave_out("two rows carry the same physical identity")
                decision.leave_out("two rows carry the same physical identity")
                continue
            identity_taken[hits[0].db_path] = decision

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
            candidate.rejected = _consistency(decision.row, probe, others, title)
            root = _root_of(candidate.file.db_path, library.roots) or "/"
            names = path_tokens(candidate.file.db_path, root)
            tag_tokens = set(tokens(" ".join(probe.tags.get(k, "") for k in ("album", "title"))))
            candidate.same_dir = os.path.dirname(candidate.file.host_path) == old_parent
            candidate.title_in_path = coverage(title, names) >= TITLE_COVERAGE
            candidate.title_in_tags = bool(tag_tokens) and coverage(title, tag_tokens) >= TITLE_COVERAGE
            candidate.author_in_path = any(coverage(a, names) >= TITLE_COVERAGE for a in authors)

    # Pass 2: choose.
    for decision in pending:
        consistent = decision.consistent
        if decision.candidates and decision.candidates[0].identity_match:
            if not consistent:
                decision.leave_out(f"physical identity matches but {decision.candidates[0].rejected}")
                continue
            decision.chosen, decision.confidence, decision.via = consistent[0], "identity", "physical identity"
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
            wanted = part_number(decision.row.path)
            tracks = [c.probe.track if c.probe else None for c in consistent]
            if wanted is None or any(t is None for t in tracks):
                decision.leave_out(f"{len(consistent)} equally consistent candidates and no part number/track "
                                   "tags to separate them")
                continue
            by_track = [c for c in consistent if c.probe and c.probe.track == wanted]
            by_both = [c for c in by_track if part_number(c.file.db_path) == wanted]
            if len(by_track) != 1 or len(by_both) != 1:
                decision.leave_out(f"{len(consistent)} equally consistent candidates; part {wanted} is not "
                                   "matched by exactly one file whose name number and track tag both agree")
                continue
            chosen, via = by_both[0], "part number and track tag"
        if not chosen.path_evidence:
            decision.leave_out(f"only candidate {chosen.file.db_path} has no folder/title/tag evidence "
                               "linking it to this book (review)")
            decision.candidates = [chosen, *[c for c in decision.candidates if c is not chosen]]
            continue
        decision.chosen, decision.confidence, decision.via = chosen, "high", via

    # Pass 3: one file, one row; and a file that an unresolved row could own is not given away.
    while True:
        changed = False
        chosen_by: dict[str, list[RowDecision]] = defaultdict(list)
        for decision in decisions:
            if decision.chosen is not None:
                chosen_by[decision.chosen.file.db_path].append(decision)
        for path, owners in chosen_by.items():
            if len(owners) > 1:
                for contender in owners:
                    contender.leave_out(f"{path} was chosen by {len(owners)} rows")
                changed = True
        for decision in decisions:
            if decision.chosen is None or decision.confidence == "identity":
                continue
            path = decision.chosen.file.db_path
            contenders = [d for d in decisions if d is not decision
                          and d.confidence != "identity"
                          and any(c.file.db_path == path and c.rejected is None for c in d.candidates)
                          and (d.chosen is None or d.chosen.file.db_path == path)]
            if contenders:
                decision.leave_out(f"{path} is also a consistent candidate of unresolved row(s) "
                                   f"{', '.join(str(d.row.id) for d in contenders)}")
                changed = True
        if not changed:
            break

    for decision in decisions:
        if decision.chosen is None:
            continue
        stored_identity = identity.parse_stored(str(decision.row.values.get("PhysicalObjectIdentity") or ""))
        keep_identity[decision.row.id] = decision.confidence == "identity" and stored_identity is not None
        if hash_chosen and decision.chosen.file.host_path not in decision.audio_hashes:
            decision.audio_hashes[decision.chosen.file.host_path] = media.audio_hash(decision.chosen.file.host_path)

    book_updates = _book_updates(library, path_map, decisions, files_by_book, flags)
    scan_safe = _scan_safe_books(library, path_map, decisions, files_by_book, stale)
    move_safe = _move_safe_books(library, decisions, files_by_book, book_updates)
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
            root = _root_of(new_base, library.roots) if new_base else None
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
