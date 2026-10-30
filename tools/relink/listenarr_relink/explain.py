"""Counts-only breakdown of the "tags name another library book" veto in a written plan.

Reads the plan, the database snapshot PLAN took, and the album/title tags of each vetoed candidate again
(ffprobe, read-only). It prints counts only, never a title, a name or a path, so the output can leave the
host. For every (book, other book) pair a vetoed candidate names it reports whether the other title is one
token, whether the tags also carry this book's whole title (and whether a tag names it exactly), whether
the other title's extra words are this book's authors or series (and with the subtitle added), and whether
physical identity matched; and how many vetoed candidates and rows this version's rule would clear.

Rows withheld by ``--exclude-file-ids`` carry the veto inside their reason and are counted separately.
Opening the snapshot read-only still lets SQLite touch its ``-shm`` index when the copy is in WAL mode (the
file's mtime changes); nothing else is written.
"""

from __future__ import annotations

import json
import re
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from listenarr_relink import inventory, media
from listenarr_relink.inventory import BookRow
from listenarr_relink.matcher import (
    EXCLUDED,
    explained_by_own_book,
    other_book_named,
    own_title_tagged,
    tag_tokens,
    veto_keys,
)
from listenarr_relink.paths import PathMap, names_title, tokens
from listenarr_relink.plan_io import file_sha256

VETO = re.compile(r"^tags name another library book \(audiobook (\d+)\)$")
VETO_IN_REASON = re.compile(r"tags name another library book \(audiobook \d+\)$")
VETO_IN_EXCLUDED_REASON = re.compile(rf"^{re.escape(EXCLUDED)} \(otherwise left out: .*tags name another library "
                                     r"book \(audiobook \d+\)\)$")
FLAGS = ("identity", "one_token", "own_title_tagged", "own_title_tag_exact", "explained_authors_series",
         "explained_with_subtitle")


@dataclass(frozen=True)
class Observation:
    """One vetoed candidate's view of one (book, other book) pair."""

    pair: tuple[int, int]
    flags: tuple[bool, ...]


def _yes_no(values: set[bool]) -> str:
    return "mixed" if len(values) > 1 else ("Y" if values.pop() else "N")


def _with_subtitle(title_tokens: frozenset[str], own: BookRow) -> bool:
    words = {t for text in [*own.own_text, own.context.get("Subtitle", "")] for t in tokens(text)}
    return title_tokens - frozenset(tokens(own.title)) <= words


def explain_veto(plan_path: Path, path_map: PathMap | None, workers: int) -> list[str]:
    """Counts lines for the vetoes in ``plan_path``; ValueError when its database snapshot changed."""
    plan: dict[str, Any] = json.loads(plan_path.read_text(encoding="utf-8"))
    path_map = path_map or PathMap.parse(plan.get("path_map", []))
    db_copy = Path(plan["db_copy"]["path"])
    if file_sha256(db_copy) != plan["db_copy"]["sha256"]:
        raise ValueError("the plan's database snapshot no longer matches its recorded sha256")
    connection = inventory.open_read_only(str(db_copy))
    try:
        present = {str(r["name"]) for r in connection.execute("PRAGMA table_info(Audiobooks)")}
        _, books, _ = inventory.load_rows(connection)
    finally:
        connection.close()
    keys = veto_keys(books)
    key_of = {book_id: key for key, book_id in keys.items()}

    vetoed: list[tuple[dict[str, Any], dict[str, Any], int]] = []
    for item in plan["left_out"]:
        for candidate in item["candidates"]:
            found = VETO.match(str(candidate.get("rejected") or ""))
            if found:
                vetoed.append((item, candidate, int(found.group(1))))
    host_paths = sorted({path_map.to_host(c["path"]) for _, c, _ in vetoed})
    with ThreadPoolExecutor(max_workers=workers) as pool:
        probes = dict(zip(host_paths, pool.map(media.probe, host_paths), strict=True))

    counts: Counter[str] = Counter()
    reason_pairs: set[tuple[int, int]] = set()
    observations: list[Observation] = []
    rows_vetoed: set[int] = set()
    rows_still_vetoed: set[int] = set()
    for item, candidate, reason_book in vetoed:
        own_id, row_id = int(item["audiobook_id"]), int(item["row_id"])
        rows_vetoed.add(row_id)
        reason_pairs.add((own_id, reason_book))
        identity_match = bool(candidate["identity_match"])
        counts["candidates_vetoed"] += 1
        counts[f"candidates_vetoed_identity_{'Y' if identity_match else 'N'}"] += 1
        own = books.get(own_id)
        probe = probes[path_map.to_host(candidate["path"])]
        if own is None or probe.error:
            counts["candidates_unreadable_now_or_book_gone"] += 1
            rows_still_vetoed.add(row_id)
            continue
        others = {k: v for k, v in keys.items() if v != own_id}
        if other_book_named(probe.tags, others, own, False) != reason_book:
            counts["candidates_strict_rule_not_reproduced_tags_changed"] += 1
        if other_book_named(probe.tags, others, own, identity_match) is None:
            counts["candidates_cleared_by_this_version"] += 1
        else:
            rows_still_vetoed.add(row_id)
        tagged = tag_tokens(probe.tags)
        own_tokens = frozenset(tokens(own.title))
        exact = any(names_title(probe.tags[k], own.title, own.authors) for k in ("album", "title")
                    if probe.tags.get(k))
        for other_id in sorted({v for k, v in others.items() if k <= tagged and not k <= own_tokens}):
            key = key_of[other_id]
            observations.append(Observation((own_id, other_id), (
                identity_match, len(key) == 1, own_title_tagged(tagged, own), exact,
                explained_by_own_book(key, own), _with_subtitle(key, own))))

    by_pair: dict[tuple[int, int], list[Observation]] = defaultdict(list)
    for observation in observations:
        by_pair[observation.pair].append(observation)
    table: Counter[str] = Counter()
    for pair_observations in by_pair.values():
        cells = [_yes_no({o.flags[i] for o in pair_observations}) for i in range(len(FLAGS))]
        table[" ".join(f"{name}={cell}" for name, cell in zip(FLAGS, cells, strict=True))] += 1
    rows_with_veto_reason = sum(1 for item in plan["left_out"] if VETO_IN_REASON.search(str(item["reason"])))
    rows_excluded_with_veto = sum(1 for item in plan["left_out"]
                                  if VETO_IN_EXCLUDED_REASON.match(str(item["reason"])))
    lines = [
        f"series_column_present {'Series' in present}",
        f"subtitle_column_present {'Subtitle' in present}",
        f"rows_left_out_with_veto_reason {rows_with_veto_reason}",
        f"rows_excluded_with_veto_reason {rows_excluded_with_veto}",
        f"rows_with_veto_reason_total {rows_with_veto_reason + rows_excluded_with_veto}",
        f"rows_with_a_vetoed_candidate {len(rows_vetoed)}",
        f"books_vetoed {len({own for own, _ in reason_pairs})}",
        f"other_books_named_in_reasons {len({other for _, other in reason_pairs})}",
        f"pairs_from_reasons {len(reason_pairs)}",
        f"pairs_named_now_strict {len(by_pair)}",
        f"pairs_from_reasons_not_named_now {len(reason_pairs - set(by_pair))}",
        *(f"{name} {counts[name]}" for name in ("candidates_vetoed", "candidates_vetoed_identity_Y",
                                                "candidates_vetoed_identity_N",
                                                "candidates_unreadable_now_or_book_gone",
                                                "candidates_strict_rule_not_reproduced_tags_changed",
                                                "candidates_cleared_by_this_version")),
        f"rows_whose_every_vetoed_candidate_clears {len(rows_vetoed - rows_still_vetoed)} "
        "(a cleared veto is not a relink: re-run PLAN for that)",
        "pairs_by_class (Y/N, or mixed across a pair's candidates):",
        *(f"  {count:6d} {cls}" for cls, count in sorted(table.items(), key=lambda kv: (-kv[1], kv[0]))),
        "observations_by_flag (candidate x named other book):",
        *(f"  {name}_Y {sum(1 for o in observations if o.flags[i])} of {len(observations)}"
          for i, name in enumerate(FLAGS)),
    ]
    return lines
