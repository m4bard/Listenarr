"""Plan file: what APPLY may do, with the evidence and the preconditions it must re-check."""

from __future__ import annotations

import hashlib
import json
from collections import Counter
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from listenarr_relink import PLAN_FORMAT_VERSION, TOOL_NAME
from listenarr_relink.inventory import Library
from listenarr_relink.matcher import Candidate, MatchResult, RowDecision
from listenarr_relink.paths import PathMap

LEFT_OUT_PREVIEW = 200


def file_sha256(path: Path) -> str:
    """SHA-256 of a file's bytes."""
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _candidate(candidate: Candidate) -> dict[str, Any]:
    probe = candidate.probe
    return {
        "path": candidate.file.db_path,
        "size": candidate.file.size,
        "rejected": candidate.rejected,
        "identity_match": candidate.identity_match,
        "same_dir": candidate.same_dir,
        "title_in_path": candidate.title_in_path,
        "title_in_tags": candidate.title_in_tags,
        "author_in_path": candidate.author_in_path,
        "duration": probe.duration if probe else None,
        "codec": probe.codec if probe else None,
        "track": probe.track if probe else None,
    }


def _relink(decision: RowDecision, keep_identity: bool, book_title: str) -> dict[str, Any]:
    chosen = decision.chosen
    assert chosen is not None
    probe = chosen.probe
    return {
        "row_id": decision.row.id,
        "audiobook_id": decision.row.audiobook_id,
        "book_title": book_title,
        "old_path": decision.row.path,
        "new_path": chosen.file.db_path,
        "host_new_path": chosen.file.host_path,
        "confidence": decision.confidence,
        "via": decision.via,
        "keep_physical_identity": keep_identity,
        "evidence": {
            "stored_size": decision.row.size,
            "file_size": chosen.file.size,
            "stored_duration": decision.row.values.get("DurationSeconds"),
            "file_duration": probe.duration if probe else None,
            "stored_codec": decision.row.values.get("Codec"),
            "file_codec": probe.codec if probe else None,
            "identity_match": chosen.identity_match,
            "same_dir": chosen.same_dir,
            "title_in_path": chosen.title_in_path,
            "title_in_tags": chosen.title_in_tags,
            "author_in_path": chosen.author_in_path,
            "track": probe.track if probe else None,
            "audio_sha256": decision.audio_hashes.get(chosen.file.host_path),
            "candidates_considered": len(decision.candidates),
        },
        "precondition": {
            "row_sha256": decision.row.digest(),
            "file": {"dev": chosen.file.dev, "ino": chosen.file.ino, "size": chosen.file.size,
                     "mtime_ns": chosen.file.mtime_ns, "ctime_ns": chosen.file.ctime_ns, "nlink": chosen.file.nlink},
        },
    }


def build_plan(library: Library, result: MatchResult, path_map: PathMap, db_copy: Path,
               live_db: str) -> dict[str, Any]:
    """Assemble the plan document."""
    titles = {book_id: book.title for book_id, book in library.books.items()}
    relinks = [_relink(d, result.keep_identity.get(d.row.id, False), titles.get(d.row.audiobook_id, ""))
               for d in result.decisions if d.chosen]
    left_out = [{
        "row_id": d.row.id,
        "audiobook_id": d.row.audiobook_id,
        "book_title": titles.get(d.row.audiobook_id, ""),
        "old_path": d.row.path,
        "reason": d.reason,
        "candidates": [_candidate(c) for c in d.candidates],
        "audio_sha256": d.audio_hashes,
    } for d in result.decisions if d.chosen is None]
    book_updates = [{
        "audiobook_id": u.book.id,
        "book_title": u.book.title,
        "set": u.set_values,
        "was": {k: u.book.values.get(k) for k in u.set_values},
        "precondition": {"row_sha256": u.book.digest()},
    } for u in result.book_updates]
    confidence = Counter(r["confidence"] for r in relinks)
    reasons = Counter(_reason_class(str(item["reason"])) for item in left_out)
    flags = Counter(f.kind for f in result.flags)
    return {
        "tool": TOOL_NAME,
        "format": PLAN_FORMAT_VERSION,
        "created_utc": datetime.now(UTC).isoformat(timespec="seconds"),
        "source_db": live_db,
        "db_copy": {"path": str(db_copy), "sha256": file_sha256(db_copy)},
        "roots": library.roots,
        "path_map": [f"{a}={b}" for a, b in path_map.pairs],
        "summary": {
            "file_rows": len(library.files),
            "live_rows": result.live_rows,
            "stale_rows": len(result.decisions),
            "relinks": len(relinks),
            "relinks_by_confidence": dict(sorted(confidence.items())),
            "left_out": len(left_out),
            "left_out_by_reason": dict(sorted(reasons.items())),
            "book_updates": len(book_updates),
            "flags": dict(sorted(flags.items())),
            "scan_safe_books": len(result.scan_safe_books),
            "move_safe_books": len(result.move_safe_books),
            "unclaimed_audio_files_not_considered": len(result.orphans),
            "symlinks_skipped": len(library.symlinks),
            "walk_errors": len(library.walk_errors),
        },
        "relinks": relinks,
        "book_updates": book_updates,
        "left_out": left_out,
        "flags": [{"kind": f.kind, "row_id": f.row_id, "audiobook_id": f.audiobook_id, "detail": f.detail}
                  for f in result.flags],
        "scan_safe_books": result.scan_safe_books,
        "move_safe_books": {str(k): v for k, v in sorted(result.move_safe_books.items())},
        "walk_errors": library.walk_errors,
    }


def _reason_class(reason: str) -> str:
    for marker, label in (("equally consistent", "ambiguous"), ("chosen by", "contended"),
                          ("also a consistent candidate", "contested"), ("no folder/title/tag", "weak-evidence"),
                          ("no unclaimed file", "no-candidate"), ("agrees with the stored", "no-consistent-candidate"),
                          ("no usable stored size", "no-size"), ("hard links", "hard-link"),
                          ("size changed", "identity-content-changed"), ("same physical identity", "identity-dup"),
                          ("physical identity matches but", "identity-inconsistent")):
        if marker in reason:
            return label
    return "other"


def write_outputs(plan: dict[str, Any], out_dir: Path, orphans: list[str]) -> tuple[Path, str]:
    """Write plan.json, plan-summary.txt, scan-books.txt and orphans.txt; return the plan path and its sha256."""
    plan_path = out_dir / "plan.json"
    plan_path.write_text(json.dumps(plan, indent=1, sort_keys=False) + "\n", encoding="utf-8")
    plan_sha = file_sha256(plan_path)
    (out_dir / "scan-books.txt").write_text("".join(f"{i}\n" for i in plan["scan_safe_books"]), encoding="utf-8")
    (out_dir / "move-books.tsv").write_text("".join(f"{i}\t{p}\n" for i, p in plan["move_safe_books"].items()),
                                            encoding="utf-8")
    (out_dir / "orphans.txt").write_text("".join(f"{p}\n" for p in orphans), encoding="utf-8")
    summary = plan["summary"]
    lines = [
        f"plan {plan_path.name} sha256 {plan_sha}",
        f"created {plan['created_utc']}  db copy sha256 {plan['db_copy']['sha256']}",
        f"file rows {summary['file_rows']}: live {summary['live_rows']}, stale {summary['stale_rows']}",
        f"relinks {summary['relinks']} by confidence {summary['relinks_by_confidence']}",
        f"book updates {summary['book_updates']}",
        f"left out {summary['left_out']} by reason {summary['left_out_by_reason']}",
        f"flags {summary['flags']}",
        f"scan-safe books {summary['scan_safe_books']}; move-safe books {summary['move_safe_books']}; "
        f"unclaimed audio files never a candidate "
        f"{summary['unclaimed_audio_files_not_considered']}; symlinks skipped {summary['symlinks_skipped']}; "
        f"walk errors {summary['walk_errors']}",
        "",
        f"LEFT OUT (first {LEFT_OUT_PREVIEW}; full list in plan.json):",
    ]
    for item in plan["left_out"][:LEFT_OUT_PREVIEW]:
        lines.append(f"  row {item['row_id']} book {item['audiobook_id']}: {item['reason']}")
        lines.extend(f"      candidate {c['path']}" + (f" (rejected: {c['rejected']})" if c["rejected"] else "")
                     for c in item["candidates"][:5])
    lines.append("")
    lines.append("FLAGS:")
    lines.extend(f"  {f['kind']} row {f['row_id']} book {f['audiobook_id']}: {f['detail']}" for f in plan["flags"])
    lines.append("")
    lines.append("RELINKS (every one; content-evidence links first, physical-identity links last):")
    for item in sorted(plan["relinks"], key=lambda r: (r["confidence"] == "identity", r["audiobook_id"], r["row_id"])):
        evidence = item["evidence"]
        marks = ",".join(k for k in ("same_dir", "title_in_path", "title_in_tags", "author_in_path") if evidence[k])
        lines.append(f"  row {item['row_id']} book {item['audiobook_id']} [{item['confidence']}: {item['via']}; "
                     f"{marks or 'no path evidence'}; candidates {evidence['candidates_considered']}]")
        lines.append(f"      old {item['old_path']}")
        lines.append(f"      new {item['new_path']}")
    lines.append("")
    lines.append("BOOK UPDATES:")
    for update in plan["book_updates"]:
        for column, value in sorted(update["set"].items()):
            lines.append(f"  book {update['audiobook_id']} {column}: {update['was'].get(column)} -> {value}")
    if plan["walk_errors"]:
        lines.append("")
        lines.append("WALK ERRORS (the plan may be missing candidates; re-run PLAN as a user that can read these):")
        lines.extend(f"  {error}" for error in plan["walk_errors"])
    (out_dir / "plan-summary.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return plan_path, plan_sha
