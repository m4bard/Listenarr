"""Command line: ``plan``, ``apply``, ``verify`` and ``explain-veto``.

Exit codes: 0 success; 2 usage or refused precondition (nothing written); 3 APPLY refused at least one
entry and rolled everything back (nothing written; refusals listed in the report, re-plan), or PLAN wrote a
plan but some folders could not be read (incomplete evidence: re-run as a user that can read them);
4 APPLY aborted (nothing written).
"""

from __future__ import annotations

import argparse
import json
import logging
import os
import sqlite3
import sys
from pathlib import Path

from listenarr_relink import inventory, matcher
from listenarr_relink.apply import ApplyAborted, apply_plan, processes_holding
from listenarr_relink.explain import explain_veto
from listenarr_relink.paths import PathMap, is_within
from listenarr_relink.plan_io import build_plan, file_sha256, write_outputs
from listenarr_relink.verify import verify

log = logging.getLogger("relink")


def _setup_logging() -> None:
    logging.basicConfig(
        level=os.environ.get("RELINK_LOG_LEVEL", "INFO").upper(),
        format="%(asctime)s %(levelname)s [%(name)s] %(message)s",
        datefmt="%Y-%m-%dT%H:%M:%S%z",
        stream=sys.stderr,
    )


def _read_exclude_file(path: Path) -> frozenset[int]:
    """AudiobookFiles ids, one per line; blank lines and ``#`` comments are ignored. ValueError otherwise.

    A list naming no id is refused too: it is what a failed export leaves, and accepting it would plan as if
    nothing were excluded.
    """
    ids: set[int] = set()
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        text = line.strip()
        if not text or text.startswith("#"):
            continue
        if not text.isdecimal():
            raise ValueError(f"{path}:{number}: not a file row id: {text!r}")
        ids.add(int(text))
    if not ids:
        raise ValueError(f"{path} names no file row id")
    return frozenset(ids)


def _plan(args: argparse.Namespace) -> int:
    path_map = PathMap.parse(args.path_map)
    out_dir = Path(args.out).resolve()
    if out_dir.exists() and any(out_dir.iterdir()):
        log.error("output directory %s exists and is not empty", out_dir)
        return 2
    exclude_file = Path(args.exclude_file_ids).resolve() if args.exclude_file_ids else None
    try:
        excluded = _read_exclude_file(exclude_file) if exclude_file else frozenset()
    except (OSError, ValueError) as error:
        log.error("exclude list unusable: %s", error)
        return 2
    connection = inventory.open_read_only(args.db)
    try:
        roots, _, files = inventory.load_rows(connection)
    finally:
        connection.close()
    unknown = sorted(excluded - {row.id for row in files})
    if unknown:
        # An id that is no file row means the list was built from the wrong column or another database.
        log.error("exclude list names %d id(s) that are no AudiobookFiles row, e.g. %s", len(unknown), unknown[:5])
        return 2
    host_roots = [os.path.realpath(path_map.to_host(r)) for r in roots]
    if any(is_within(str(out_dir), root) for root in host_roots):
        log.error("output directory %s lies inside a library root; PLAN never writes there", out_dir)
        return 2
    missing_roots = [r for r in host_roots if not os.path.isdir(r)]
    if missing_roots:
        log.error("library root(s) not visible on this host: %s (use --path-map)", ", ".join(missing_roots))
        return 2
    out_dir.mkdir(parents=True, exist_ok=True)
    db_copy = out_dir / "db-copy.sqlite"
    inventory.snapshot_database(args.db, str(db_copy))
    log.info("snapshot db=%s copy=%s", args.db, db_copy)
    connection = inventory.open_read_only(str(db_copy))
    try:
        roots, books, files = inventory.load_rows(connection)
    finally:
        connection.close()
    library = inventory.Library(roots, books, files)
    try:
        for root in roots:
            if any(other != root and is_within(root, other) for other in roots):
                log.info("root %s lies inside another root; walked once as part of it", root)
                continue
            inventory.walk_root(root, path_map, library)
    except inventory.PathMapError as error:
        log.error("%s", error)
        return 2
    identity_only = not args.include_shape_matches
    result = matcher.match(library, path_map, args.workers, not args.no_hash_chosen, identity_only, excluded)
    plan = build_plan(library, result, path_map, db_copy, args.db, identity_only, excluded, exclude_file)
    plan_path, plan_sha = write_outputs(plan, out_dir, [o.db_path for o in result.orphans])
    summary = plan["summary"]
    log.info("plan=%s sha256=%s relinks=%d left_out=%d book_updates=%d scan_safe_books=%d", plan_path, plan_sha,
             summary["relinks"], summary["left_out"], summary["book_updates"], summary["scan_safe_books"])
    print(plan_sha)
    if library.walk_errors:
        log.error("%d walk errors: the plan is written but incomplete (see WALK ERRORS in plan-summary.txt)",
                  len(library.walk_errors))
        return 3
    return 0


def _apply(args: argparse.Namespace) -> int:
    plan_path = Path(args.plan).resolve()
    actual = file_sha256(plan_path)
    if actual != args.plan_sha256.lower():
        log.error("plan sha256 %s does not match the reviewed value %s", actual, args.plan_sha256)
        return 2
    if not args.confirm_service_stopped:
        log.error("APPLY needs --confirm-service-stopped: stop Listenarr first")
        return 2
    holders, uninspected = processes_holding(args.db)
    if holders:
        log.error("database is open in process(es) %s; stop the service first", holders)
        return 2
    if uninspected:
        log.warning("could not inspect %d processes (other users); relying on --confirm-service-stopped and "
                    "the write lock", uninspected)
    backup = Path(args.backup)
    try:
        connection = inventory.open_read_only(str(backup))
        try:
            check = connection.execute("PRAGMA quick_check").fetchone()[0]
            backup_rows = connection.execute("SELECT count(*) FROM AudiobookFiles").fetchone()[0]
        finally:
            connection.close()
    except Exception as error:
        log.error("backup %s is not a readable Listenarr database: %s", backup, error)
        return 2
    if check != "ok":
        log.error("backup %s fails quick_check: %s", backup, check)
        return 2
    log.info("backup=%s quick_check=ok AudiobookFiles=%d", backup, backup_rows)
    plan = json.loads(plan_path.read_text(encoding="utf-8"))
    out_dir = Path(args.out).resolve() if args.out else plan_path.parent
    out_dir.mkdir(parents=True, exist_ok=True)
    try:
        outcome = apply_plan(args.db, plan_path, plan, out_dir, args.dry_run)
    except ApplyAborted as error:
        log.error("aborted, nothing written: %s", error)
        return 4
    for refusal in outcome.refused:
        log.warning("refused %s", refusal)
    return 3 if outcome.refused else 0


def _verify(args: argparse.Namespace) -> int:
    plan = json.loads(Path(args.plan).read_text(encoding="utf-8")) if args.plan else None
    path_map = PathMap.parse(args.path_map or (plan.get("path_map", []) if plan else []))
    report = verify(args.db, path_map, plan)
    text = json.dumps(report, indent=1)
    if args.out:
        Path(args.out).write_text(text + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "stale_row_ids"}, indent=1))
    return 0


def _explain_veto(args: argparse.Namespace) -> int:
    path_map = PathMap.parse(args.path_map) if args.path_map else None
    try:
        lines = explain_veto(Path(args.plan), path_map, args.workers)
    except (OSError, ValueError, KeyError, sqlite3.Error) as error:
        log.error("cannot explain the plan's vetoes: %s", error)
        return 2
    print("\n".join(lines))
    return 0


def main(argv: list[str] | None = None) -> int:
    """Entry point."""
    _setup_logging()
    parser = argparse.ArgumentParser(prog="listenarr-relink", description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    plan = sub.add_parser("plan", help="read-only: snapshot the DB, walk the roots, write a reviewable plan")
    plan.add_argument("--db", required=True, help="Listenarr DB (opened read-only; a copy is taken)")
    plan.add_argument("--out", required=True, help="new, empty output directory outside every library root")
    plan.add_argument("--path-map", action="append", default=[], metavar="DBPREFIX=HOSTPREFIX",
                      help="translate stored paths to this host's view (repeatable)")
    plan.add_argument("--workers", type=int, default=4, help="parallel ffprobe/ffmpeg processes (default 4)")
    plan.add_argument("--no-hash-chosen", action="store_true",
                      help="skip the audio-stream hash of each chosen file (faster; hashes of ambiguous sets stay)")
    plan.add_argument("--include-shape-matches", action="store_true",
                      help="NOT RECOMMENDED: also propose shape-and-name links (size/duration/codec plus a name "
                           "naming the title, no content hash). Three adversarial reviews forced wrong links in "
                           "this mode. Default: only books whose every link is by .620 physical identity")
    plan.add_argument("--exclude-file-ids", metavar="FILE",
                      help="AudiobookFiles ids (one per line, # comments) never to re-link; every book holding "
                           "one is withheld whole, live rows or not. A list naming no id, or an id that is no "
                           "file row, refuses the run")
    plan.set_defaults(handler=_plan)

    apply = sub.add_parser("apply", help="write a reviewed plan into the stopped service's DB, one transaction")
    apply.add_argument("--db", required=True, help="the live Listenarr DB file (service stopped)")
    apply.add_argument("--plan", required=True, help="plan.json produced by `plan`")
    apply.add_argument("--plan-sha256", required=True, help="sha256 printed with the plan the operator reviewed")
    apply.add_argument("--backup", required=True, help="verified backup of the DB taken after the service stopped")
    apply.add_argument("--confirm-service-stopped", action="store_true")
    apply.add_argument("--dry-run", action="store_true", help="run every check, then roll back")
    apply.add_argument("--out", help="where to write the report and undo SQL (default: next to the plan)")
    apply.set_defaults(handler=_apply)

    check = sub.add_parser("verify", help="read-only health report; with --plan, checks the plan's targets")
    check.add_argument("--db", required=True)
    check.add_argument("--plan")
    check.add_argument("--path-map", action="append", default=[], metavar="DBPREFIX=HOSTPREFIX")
    check.add_argument("--out", help="write the full JSON report here")
    check.set_defaults(handler=_verify)

    explain = sub.add_parser("explain-veto", help="read-only, counts only: classify a plan's 'tags name another "
                                                  "library book' vetoes (never prints titles or paths)")
    explain.add_argument("--plan", required=True, help="plan.json produced by `plan` (its db-copy.sqlite is read)")
    explain.add_argument("--path-map", action="append", default=[], metavar="DBPREFIX=HOSTPREFIX",
                         help="default: the plan's own path map")
    explain.add_argument("--workers", type=int, default=4, help="parallel ffprobe processes (default 4)")
    explain.set_defaults(handler=_explain_veto)

    args = parser.parse_args(argv)
    if getattr(args, "workers", 1) < 1:
        parser.error("--workers must be at least 1")
    handler = args.handler
    result: int = handler(args)
    return result
