"""``plan --operator-links``: an operator-reviewed list of row -> in-root file links that PLAN refused for one reason.

Each line is ``row_id <TAB> target_path <TAB> reason``, the target in the service's spelling. A reason lifts exactly
one refusal for that row, and every other check still runs:

* ``folder-edition``: the row's own object (inode + handle) sits in a folder whose bracketed id is not the book's
  ``Asin`` and is no library book's ``Asin`` (another edition of the same work). Lifts ``folder-id-disagrees``;
* ``hl-edition``: the same, for an object that also has a name in a ``--quarantine-dir`` folder carrying the in-root
  id (or no id). Lifts ``hardlinked`` and the folder-id refusal together, since one implies the other here;
* ``hl-other-quarantine``: the row's own object, in the book's own-``Asin`` folder, with exactly one other name, in
  a ``--quarantine-dir`` folder whose path carries exactly one bracketed id (anywhere in it), another than the
  ``Asin`` and no other library book's (with rows or without: that is HL9's pattern, the file is that book's).
  Lifts ``hardlinked``. Anything else (two other names, no id, several ids) is ambiguous and refused;
* ``duration-decode``: the row's own object, refused only because the container's duration disagrees with the
  stored one. Lifted only when decoding the audio gives the stored duration (1 %): the container is what is wrong.

Still enforced on every link: the target is the row's own object (PLAN's identity match, on the root's dataset); no
other row stores its path, reaches it, or names it by stored identity (stale rows too); the row's duration, codec,
sample rate and channels (decoded duration for ``duration-decode``); the tags veto; no ASIN tag naming another
library book (manual import writes the target's own ASIN, so only another book's is evidence); for the two edition
reasons,
positive evidence that the content is this book (every file's tags carry its whole title and the book's summed
duration is within 10 % of its catalog runtime); and the book is linked whole, every stale row by a link, or none.

A link that fails a check refuses its book, counted by reason; the rest of the plan is written. The run itself is
refused (exit 2) for a malformed list: an unknown reason, a row that is not stale or is listed twice, a target
listed twice, a row whose class is not the one the reason lifts, or a target that is not the row's own object.
"""

from __future__ import annotations

import logging
import os
import re
from collections import Counter
from collections.abc import Iterable
from dataclasses import dataclass, field
from pathlib import Path

from listenarr_relink import identity, media, physical
from listenarr_relink.explain_identity import row_class
from listenarr_relink.inventory import FileRow, Library, lstat_or_none
from listenarr_relink.matcher import (
    Candidate,
    MatchResult,
    RowDecision,
    _book_updates,
    _consistency,
    _resolves,
    own_title_tagged,
    root_of,
    tag_tokens,
    veto_keys,
)
from listenarr_relink.paths import PathMap
from listenarr_relink.qrestore import asin_tags

REASONS = {"folder-edition": "folder-id-disagrees", "hl-edition": "hardlinked",
           "hl-other-quarantine": "hardlinked", "duration-decode": "vetoed:identity-inconsistent-duration"}
EDITION_REASONS = frozenset({"folder-edition", "hl-edition"})
DECODE_TOLERANCE = 0.01
RUNTIME_TOLERANCE = 0.10
log = logging.getLogger("relink.oplinks")
_ANY_BRACKETED_ID = re.compile(r"\[([^\[\]]+)\]")


class OperatorLinksRefused(ValueError):
    """The list itself is unusable; no plan is written."""


@dataclass(frozen=True)
class Link:
    """One reviewed line."""

    row_id: int
    target: str
    reason: str


@dataclass
class LinkOutcome:
    """What the list did: links accepted, and books refused by reason (counts only)."""

    accepted: Counter[str] = field(default_factory=Counter)
    refused_books: Counter[str] = field(default_factory=Counter)
    accepted_rows: dict[int, str] = field(default_factory=dict)  # row id -> reason

    def lines(self) -> list[str]:
        """Counts only."""
        return [*(f"operator_links_accepted {reason} {self.accepted[reason]}" for reason in REASONS),
                *(f"operator_links_refused_books {why} {count}" for why, count in sorted(self.refused_books.items()))]


def read_links(path: Path) -> list[Link]:
    """Parse the list; OperatorLinksRefused on any malformed line, an unknown reason, or no line at all."""
    links: list[Link] = []
    for number, line in enumerate(path.read_text(encoding="utf-8", errors="surrogateescape").splitlines(), start=1):
        if not line.strip() or line.startswith("#"):
            continue
        cells = line.split("\t")
        if len(cells) != 3 or not cells[0].strip().isdecimal() or not cells[1].startswith("/"):
            raise OperatorLinksRefused(f"line {number}: expected '<row id><TAB><target path><TAB><reason>'")
        if cells[2].strip() not in REASONS:
            raise OperatorLinksRefused(f"line {number}: unknown reason (one of {', '.join(REASONS)})")
        links.append(Link(int(cells[0]), cells[1], cells[2].strip()))
    if not links:
        raise OperatorLinksRefused(f"{path} names no link")
    return links


def apply_links(library: Library, path_map: PathMap, result: MatchResult, links: list[Link]) -> LinkOutcome:
    """Check every link and choose the accepted ones in ``result`` (book updates recomputed); counts returned."""
    decisions = {d.row.id: d for d in result.decisions}
    _refuse_malformed(links, decisions)
    outcome = LinkOutcome()
    no_progress_before = media.no_progress_decode_count()
    asins = {b.context.get("Asin", "").strip().casefold() for b in library.books.values()} - {""}
    others = veto_keys(library.books)
    by_book: dict[int, list[tuple[Link, RowDecision]]] = {}
    for link in links:
        decision = decisions[link.row_id]
        by_book.setdefault(decision.row.audiobook_id, []).append((link, decision))
    claims = _claims(library, path_map)
    stale_by_book: dict[int, set[int]] = {}
    for decision in result.decisions:
        stale_by_book.setdefault(decision.row.audiobook_id, set()).add(decision.row.id)
    chosen: dict[int, Candidate] = {}
    for book_id, entries in sorted(by_book.items()):
        why = None
        if {d.row.id for _, d in entries} != stale_by_book.get(book_id, set()):
            why = "book-not-whole"
        picked: dict[int, Candidate] = {}
        for link, decision in entries:
            if why is not None:
                break
            candidate = decision.candidates[0]
            why = (_claimed(candidate, decision.row, claims)
                   or _reason_check(link, decision, candidate, library, asins)
                   or _content_check(link, decision, candidate, library, others, asins))
            picked[decision.row.id] = candidate
        if why is None and any(link.reason in EDITION_REASONS for link, _ in entries):
            why = _edition_evidence(library, book_id, [picked[d.row.id] for _, d in entries])
        if why is not None:
            outcome.refused_books[why] += 1
            continue
        for link, decision in entries:
            chosen[decision.row.id] = picked[decision.row.id]
            outcome.accepted[link.reason] += 1
            outcome.accepted_rows[decision.row.id] = link.reason
    for row_id, candidate in chosen.items():
        decision = decisions[row_id]
        decision.chosen, decision.confidence, decision.reason = candidate, "identity", None
        decision.via = f"physical identity, operator link ({outcome.accepted_rows[row_id]})"
        # The stored identity may spell a drifted device; the object is pinned by APPLY's (dev, ino, ...) check.
        result.keep_identity[row_id] = False
    files_by_book: dict[int, list[FileRow]] = {}
    for row in library.files:
        files_by_book.setdefault(row.audiobook_id, []).append(row)
    result.book_updates = _book_updates(library, path_map, result.decisions, files_by_book, result.flags)
    no_progress = media.no_progress_decode_count() - no_progress_before
    if no_progress:
        log.warning("decode exited 0 without out_time_us for %d files; ffmpeg may ignore -progress", no_progress)
    return outcome


def _refuse_malformed(links: Iterable[Link], decisions: dict[int, RowDecision]) -> None:
    seen_rows: set[int] = set()
    seen_targets: set[str] = set()
    for link in links:
        if link.row_id in seen_rows or link.target in seen_targets:
            raise OperatorLinksRefused(f"row {link.row_id} or its target is listed twice")
        seen_rows.add(link.row_id)
        seen_targets.add(link.target)
        decision = decisions.get(link.row_id)
        if decision is None:
            raise OperatorLinksRefused(f"row {link.row_id} is no stale row")
        if row_class(decision) != REASONS[link.reason]:
            raise OperatorLinksRefused(f"row {link.row_id} is {row_class(decision)}, which {link.reason} does not lift")
        if not decision.candidates or decision.candidates[0].file.db_path != link.target \
                or not decision.candidates[0].identity_match:
            raise OperatorLinksRefused(f"row {link.row_id}: the target is not the row's own object")


@dataclass(frozen=True)
class _Claims:
    paths: frozenset[str]
    live_objects: frozenset[tuple[int, int]]
    stored: dict[int, list[tuple[int, identity.StoredIdentity]]]


def _claims(library: Library, path_map: PathMap) -> _Claims:
    live: set[tuple[int, int]] = set()
    stored: dict[int, list[tuple[int, identity.StoredIdentity]]] = {}
    for row in library.files:
        if row.path and _resolves(row.path, path_map):
            info = lstat_or_none(path_map.to_host(row.path))
            if info is not None:
                live.add((info.st_dev, info.st_ino))
        parsed = identity.parse_stored(str(row.values.get("PhysicalObjectIdentity") or ""))
        if parsed is not None:
            stored.setdefault(parsed.inode, []).append((row.id, parsed))
    return _Claims(frozenset(r.path for r in library.files if r.path), frozenset(live), stored)


def _claimed(candidate: Candidate, row: FileRow, claims: _Claims) -> str | None:
    hit = candidate.file
    if hit.db_path in claims.paths or (hit.dev, hit.ino) in claims.live_objects:
        return "target-already-tracked"
    others = [s for row_id, s in claims.stored.get(hit.ino, []) if row_id != row.id]
    if others:
        live = identity.read_live(hit.host_path)
        if live is None or any(live.same_object(s) or live.matches(s) for s in others):
            return "target-is-another-rows-object"
    return None


def _reason_check(link: Link, decision: RowDecision, candidate: Candidate, library: Library,
                  asins: set[str]) -> str | None:
    hit = candidate.file
    book = library.books.get(decision.row.audiobook_id)
    asin = book.context.get("Asin", "").strip().casefold() if book else ""
    in_root_id = physical.folder_id(hit.db_path, root_of(hit.db_path, library.roots))
    names = [n for n in library.quarantine.get(hit.ino, []) if n.host_path != hit.host_path]
    if link.reason in EDITION_REASONS:
        if in_root_id is None or in_root_id == asin:
            return "not-an-edition-folder"
        if in_root_id in asins:
            return "folder-names-a-library-book"
    if link.reason == "hl-edition":
        ok = len(names) == hit.nlink - 1 and all(
            n.dev == hit.dev and physical.folder_id(n.host_path, n.quarantine_dir) in (in_root_id, None) for n in names)
        return None if ok else "other-names-ambiguous"
    if link.reason == "hl-other-quarantine":
        if in_root_id != asin:
            return "not-the-own-folder"
        if hit.nlink != 2 or len(names) != 1 or names[0].dev != hit.dev:
            return "other-names-ambiguous"
        ids = {m.strip().casefold() for m in _ANY_BRACKETED_ID.findall(
            os.path.relpath(names[0].host_path, names[0].quarantine_dir))}
        if len(ids) != 1 or asin in ids:
            return "other-names-ambiguous"
        # The quarantine name naming another library book (rows or none) is HL9's pattern: Y's file, not X's.
        return "names-another-library-book" if ids & asins else None
    return None


def _content_check(link: Link, decision: RowDecision, candidate: Candidate, library: Library,
                   others: dict[frozenset[str], int], asins: set[str]) -> str | None:
    if candidate.probe is None:
        candidate.probe = media.probe(candidate.file.host_path)
    probe = candidate.probe
    owner = library.books.get(decision.row.audiobook_id)
    own = owner.context.get("Asin", "").strip().casefold() if owner else ""
    if asin_tags(probe.tags) & (asins - {own}):
        return "asin-tag-names-another-library-book"
    other_titles = {k: v for k, v in others.items() if v != decision.row.audiobook_id}
    if link.reason == "duration-decode":
        stored = float(str(decision.row.values.get("DurationSeconds") or 0))
        decoded = media.decode_duration(candidate.file.host_path)
        if decoded is None or stored <= 0 or abs(decoded - stored) > DECODE_TOLERANCE * stored:
            return "decode-differs-from-stored"
        probe = media.Probe(decoded, probe.codec, probe.sample_rate, probe.channels, probe.bit_rate, probe.tags,
                            probe.error)
    reason = _consistency(decision.row, probe, other_titles, owner, True)
    return None if reason is None else ("tags-name-another-book" if "another library book" in reason
                                        else "row-content-disagrees")


def _edition_evidence(library: Library, book_id: int, candidates: list[Candidate]) -> str | None:
    """Every file's tags carry the book's whole title, and the summed duration is within 10 % of its runtime."""
    book = library.books.get(book_id)
    if book is None or not all(c.probe and own_title_tagged(tag_tokens(c.probe.tags), book) for c in candidates):
        return "no-own-title-evidence"
    try:
        expected = float(book.context.get("Runtime") or 0) * 60
    except ValueError:
        expected = 0.0
    durations = [c.probe.duration for c in candidates if c.probe]
    if expected <= 0 or len(durations) != len(candidates) or None in durations:
        return "runtime-unknown"
    total = sum(d for d in durations if d is not None)
    return None if abs(total - expected) <= RUNTIME_TOLERANCE * expected else "runtime-misfit"

