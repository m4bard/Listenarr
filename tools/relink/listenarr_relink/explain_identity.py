"""Counts-only classification of every stale row by what its stored physical identity finds on disk.

Runs the same matching PLAN runs (identity-only, no hashing) and writes no plan. Each stale row falls in
exactly one class:

* ``matched``: PLAN would re-link it by physical identity. Two subsets are counted apart:
  ``matched_hardlinked_to_quarantine`` (a hard-linked object whose every other name is a quarantine name of
  this book) and ``matched_tags_disagree_folder_agrees`` (tags name another book, waived because the file
  handle and the folder's ``Asin`` agree);
* ``vetoed:<reason>``: its identity matched one file, but a later rule left it out (audio facts, tags naming
  another book, the book left whole, ...);
* ``ambiguous-shared-by-rows`` / ``ambiguous-shared-by-files``: more than one row or file carries the identity;
* ``hardlinked``: the object has more than one name;
* ``cross-pool`` / ``other-dataset-same-pool``: the identity matched only off the library root's dataset;
* ``folder-id-disagrees``: the matched file's folder carries another bracketed id than this book's;
* ``handle-mismatch`` (inode reused), ``handle-unreadable``, ``no-candidate`` (no unclaimed file has the
  inode), ``no-stored-identity``: no object found; such a row may still be matched by size
  (``matched-by-shape`` or ``not-identity:<reason>``).

The summary printed holds counts only, never a title, a name or a path, so it can leave the host. The
optional per-row file holds ids and classes, plus paths and reasons only when asked for.
"""

from __future__ import annotations

from collections import Counter

from listenarr_relink import identity, physical
from listenarr_relink.inventory import Library
from listenarr_relink.matcher import MatchResult, RowDecision, root_of
from listenarr_relink.plan_io import reason_class

IDENTITY_CLASSES = (physical.AMBIGUOUS_ROWS, physical.AMBIGUOUS_FILES, physical.HARDLINKED, physical.CROSS_POOL,
                    physical.OTHER_DATASET, physical.FOLDER_ID_DISAGREES, physical.HANDLE_MISMATCH,
                    physical.HANDLE_UNREADABLE, physical.NO_CANDIDATE, physical.NO_STORED)
# Sub-reasons of "physical identity matches but ...": the consistency check that failed.
_INCONSISTENT = (("which stands in for it", "placeholder-duration-unreadable"),
                 ("but tags name another library book", "tags-name-another-book"), ("but duration ", "duration"),
                 ("but stored duration ", "duration"), ("but codec ", "codec"), ("but SampleRate ", "stream"),
                 ("but Channels ", "stream"), ("but not readable as audio", "unreadable-audio"))


def _veto(reason: str) -> str:
    label = reason_class(reason)
    if label == "identity-inconsistent":
        return next((f"{label}-{sub}" for marker, sub in _INCONSISTENT if marker in reason), label)
    return label


def row_class(decision: RowDecision) -> str:
    """The one class a stale row falls in."""
    if decision.identity_class == physical.MATCH:
        if decision.chosen is not None and decision.confidence == "identity":
            return "matched"
        return f"vetoed:{_veto(decision.reason or '')}"
    if decision.chosen is not None:
        return "matched-by-shape"
    if decision.identity_class in physical.FALL_THROUGH or decision.identity_class == physical.NO_STORED:
        return f"{decision.identity_class}:not-identity:{reason_class(decision.reason or '')}"
    return str(decision.identity_class)


def _summary_class(name: str) -> str:
    """Fold the size-path tail of a no-object class into its identity class for the headline counts."""
    return name.split(":not-identity:")[0]


def _waivers(decision: RowDecision) -> str:
    """Which narrowed rule a row's identity candidate passed by, whatever its final class ('-' for none)."""
    found = decision.candidates[0] if decision.candidates else None
    names = [name for name, used in (("hardlinked-to-quarantine", found is not None and found.hardlinked_to_quarantine),
                                     ("tags-disagree-folder-agrees", found is not None and found.tags_waived)) if used]
    return ",".join(names) or "-"


def row_lines(result: MatchResult, with_paths: bool) -> list[str]:
    """One tab-separated line per stale row: ids, class, drift, folder id, waivers (and paths, reason if asked)."""
    header = ["row_id", "audiobook_id", "class", "device_drift", "folder_id", "waivers"]
    lines = ["\t".join([*header, *(["stored_path", "matched_path", "reason"] if with_paths else [])])]
    for decision in sorted(result.decisions, key=lambda d: d.row.id):
        found = decision.candidates[0] if decision.candidates else None
        cells = [str(decision.row.id), str(decision.row.audiobook_id), row_class(decision),
                 "Y" if found is not None and found.device_drift else "N", decision.folder_id or "-",
                 _waivers(decision)]
        if with_paths:
            cells += [decision.row.path, found.file.db_path if found else "", decision.reason or ""]
        lines.append("\t".join(cell.replace("\t", " ").replace("\n", " ") for cell in cells))
    return lines


def _all_names_seen(decision: RowDecision) -> bool:
    return bool(decision.candidates) and len(decision.candidates) >= decision.candidates[0].file.nlink


def _rows_sharing_only_an_inode(library: Library, result: MatchResult) -> int:
    """Rows refused for a shared inode number whose file handle no other row carries (the stricter rule)."""
    stored = [identity.parse_stored(str(r.values.get("PhysicalObjectIdentity") or "")) for r in library.files]
    handles = Counter((s.inode, s.handle) for s in stored if s is not None and s.handle)
    count = 0
    for decision in result.decisions:
        if decision.identity_class != physical.AMBIGUOUS_ROWS:
            continue
        own = identity.parse_stored(str(decision.row.values.get("PhysicalObjectIdentity") or ""))
        if own is not None and own.handle and handles[(own.inode, own.handle)] == 1:
            count += 1
    return count


def _stored_folder_agrees(library: Library, decision: RowDecision) -> bool:
    """True for a folder-id refusal whose stored path carries the same bracketed id as the matched folder."""
    if not decision.candidates:
        return False
    own = physical.folder_id(decision.row.path, root_of(decision.row.path, library.roots))
    found = decision.candidates[0].file.db_path
    return own is not None and own == physical.folder_id(found, root_of(found, library.roots))


def explain_identity(library: Library, result: MatchResult) -> list[str]:
    """Counts lines: every class, the identity totals to compare with an earlier inode count, and subsets."""
    classes = Counter(row_class(d) for d in result.decisions)
    headline = Counter(_summary_class(name) for name in classes.elements())
    matched = [d for d in result.decisions if row_class(d) == "matched"]
    identity_matched = sum(n for name, n in headline.items() if name == "matched" or name.startswith("vetoed:"))
    handle_equal = identity_matched + sum(headline[name] for name in (
        physical.AMBIGUOUS_FILES, physical.HARDLINKED, physical.CROSS_POOL, physical.OTHER_DATASET,
        physical.FOLDER_ID_DISAGREES))
    hardlinked = [d for d in result.decisions if d.identity_class == physical.HARDLINKED]
    waived = Counter(name for d in result.decisions for name in _waivers(d).split(","))
    stale_by_book: dict[int, list[RowDecision]] = {}
    for decision in result.decisions:
        stale_by_book.setdefault(decision.row.audiobook_id, []).append(decision)
    fixed = ["matched", *IDENTITY_CLASSES]
    stored_folder_agrees = sum(1 for d in result.decisions if d.identity_class == physical.FOLDER_ID_DISAGREES
                               and _stored_folder_agrees(library, d))
    lines = [
        f"stale_rows {len(result.decisions)}",
        f"walk_errors {len(library.walk_errors)}",
        "classes (each stale row in exactly one):",
        *(f"  {name} {headline[name]}" for name in fixed),
        *(f"  {name} {count}" for name, count in sorted(headline.items()) if name not in fixed),
        f"identity_matched_before_vetoes {identity_matched} (matched + vetoed:*)",
        f"handle_equal_unclaimed_rows {handle_equal} (identity_matched_before_vetoes + ambiguous-shared-by-files "
        "+ hardlinked + cross-pool + other-dataset-same-pool + folder-id-disagrees)",
        f"matched_device_drift {sum(1 for d in matched if d.chosen and d.chosen.device_drift)}",
        f"matched_same_device {sum(1 for d in matched if d.chosen and not d.chosen.device_drift)}",
        f"matched_hardlinked_to_quarantine {sum(1 for d in matched if d.chosen and d.chosen.hardlinked_to_quarantine)}",
        f"matched_tags_disagree_folder_agrees {sum(1 for d in matched if d.chosen and d.chosen.tags_waived)}",
        f"hardlinked_to_quarantine_before_vetoes {waived['hardlinked-to-quarantine']}",
        f"tags_disagree_folder_agrees_waived {waived['tags-disagree-folder-agrees']}",
        f"quarantine_names_with_links {sum(len(names) for names in library.quarantine.values())}",
        *(f"matched_folder_id_{state} {sum(1 for d in matched if d.folder_id == state)}"
          for state in ("agrees", "unknown")),
        f"hardlinked_every_name_unclaimed_under_the_roots {sum(1 for d in hardlinked if _all_names_seen(d))}",
        f"hardlinked_a_name_elsewhere {sum(1 for d in hardlinked if not _all_names_seen(d))}",
        f"ambiguous_rows_whose_file_handle_is_unique {_rows_sharing_only_an_inode(library, result)}",
        f"folder_id_disagrees_but_the_stored_folder_id_agrees {stored_folder_agrees}",
        f"devices_under_the_roots {len({f.dev for f in library.disk} | set(library.root_devices.values()))}",
        f"books_with_a_stale_row {len(stale_by_book)}",
        "books_whose_every_stale_row_is_matched "
        f"{sum(1 for ds in stale_by_book.values() if all(row_class(d) == 'matched' for d in ds))}",
        "size_path_tails (no object found; what the size path then did):",
        *(f"  {name} {count}" for name, count in sorted(classes.items()) if ":not-identity:" in name),
    ]
    return lines
