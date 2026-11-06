"""Command line: ``plan``, ``apply``, ``verify``, ``explain-veto``, ``explain-identity`` and the quarantine restore.

The restore commands are ``restore-explain`` (read-only, counts only), ``restore-plan`` (read-only, writes a move
plan), ``restore-move`` and ``restore-undo`` (the only commands that change library files; see ``qrestore``).

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

from listenarr_relink import identity, inventory, matcher, qrestore
from listenarr_relink.apply import ApplyAborted, apply_plan, processes_holding
from listenarr_relink.explain import explain_veto
from listenarr_relink.explain_identity import explain_identity, row_lines
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


def _quarantine_dirs(values: list[str] | None, host_roots: list[str]) -> list[str] | None:
    """The ``--quarantine-dir`` folders, resolved; None (logged) when one is missing or overlaps a root or another.

    Each value is taken literally (no globbing). A folder inside a library root, or holding one, is refused:
    its names would be library names, not quarantine names. So is one at or above a mount point: a bind mount
    could show library folders, or one folder twice, inside it.
    """
    mounts = identity.mount_points()
    folders = [os.path.realpath(value) for value in values or []]
    if folders and mounts is None:
        log.error("--quarantine-dir needs the mount table (/proc/self/mountinfo), which could not be read")
        return None
    for folder in folders:
        if not os.path.isdir(folder):
            log.error("--quarantine-dir %s is not a folder", folder)
            return None
        if any(is_within(folder, root) or is_within(root, folder) for root in host_roots):
            log.error("--quarantine-dir %s lies inside, or holds, a library root", folder)
            return None
        if any(is_within(point, folder) for point in mounts or []):
            log.error("--quarantine-dir %s is, or holds, a mount point", folder)
            return None
        if sum(1 for other in folders if is_within(folder, other)) > 1:
            log.error("--quarantine-dir %s is given twice, or lies inside another one", folder)
            return None
    return folders


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
    quarantine_dirs = _quarantine_dirs(args.quarantine_dir, host_roots)
    if quarantine_dirs is None:
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
    if not _walk(library, path_map, quarantine_dirs):
        return 2
    identity_only = not args.include_shape_matches
    result = matcher.match(library, path_map, args.workers, not args.no_hash_chosen, identity_only, excluded,
                           args.tags_waiver)
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


def _walk(library: inventory.Library, path_map: PathMap, quarantine_dirs: list[str],
          restore_dirs: list[str] | None = None) -> bool:
    """Record each root's device and every mount's pool, walk the roots, the quarantine and the restore folders.

    False on a bad path map, or a quarantine or restore folder that reaches a folder already walked.
    """
    library.root_devices = {r: os.stat(path_map.to_host(r)).st_dev for r in library.roots}
    library.device_pools = identity.mount_pools()
    try:
        for root in library.roots:
            if any(other != root and is_within(root, other) for other in library.roots):
                log.info("root %s lies inside another root; walked once as part of it", root)
                continue
            inventory.walk_root(root, path_map, library)
    except inventory.PathMapError as error:
        log.error("%s", error)
        return False
    try:
        for folder in quarantine_dirs:
            inventory.walk_quarantine(folder, library)
        for folder in restore_dirs or []:
            inventory.walk_restore_source(folder, library)
    except inventory.QuarantineAliasError as error:
        log.error("%s", error)
        return False
    return True


def _explain_identity(args: argparse.Namespace) -> int:
    path_map = PathMap.parse(args.path_map)
    connection = inventory.open_read_only(args.db)
    try:
        roots, books, files = inventory.load_rows(connection)
    finally:
        connection.close()
    host_roots = [os.path.realpath(path_map.to_host(r)) for r in roots]
    missing_roots = [r for r in host_roots if not os.path.isdir(r)]
    if missing_roots:
        log.error("%d library root(s) not visible on this host (use --path-map)", len(missing_roots))
        return 2
    quarantine_dirs = _quarantine_dirs(args.quarantine_dir, host_roots)
    if quarantine_dirs is None:
        return 2
    if args.with_paths and not args.rows_out:
        log.error("--with-paths needs --rows-out: paths never go to stdout")
        return 2
    rows_out = Path(args.rows_out).resolve() if args.rows_out else None
    if rows_out is not None and (rows_out.exists() or not rows_out.parent.is_dir()
                                 or any(is_within(str(rows_out), r) for r in host_roots)):
        log.error("--rows-out must be a new file, in an existing folder, outside every library root")
        return 2
    library = inventory.Library(roots, books, files)
    if not _walk(library, path_map, quarantine_dirs):
        return 2
    result = matcher.match(library, path_map, args.workers, hash_chosen=False, identity_only=True,
                           tags_waiver=args.tags_waiver)
    if rows_out is not None:
        rows_out.write_text("\n".join(row_lines(result, args.with_paths)) + "\n", encoding="utf-8")
    print("\n".join(explain_identity(library, result)))
    return 3 if library.walk_errors else 0


def _restore_survey(args: argparse.Namespace) -> tuple[inventory.Library, qrestore.Survey, PathMap] | None:
    """Load the DB, check and walk the roots, the quarantine and the restore folders, and survey; None: refused."""
    path_map = PathMap.parse(args.path_map)
    connection = inventory.open_read_only(args.db)
    try:
        roots, books, files = inventory.load_rows(connection)
    finally:
        connection.close()
    host_roots = [os.path.realpath(path_map.to_host(r)) for r in roots]
    if [r for r in host_roots if not os.path.isdir(r)]:
        log.error("library root(s) not visible on this host (use --path-map)")
        return None
    quarantine = args.quarantine_dir or []
    folders = _quarantine_dirs([*quarantine, *args.restore_from], host_roots)
    if folders is None:
        return None
    library = inventory.Library(roots, books, files)
    if not _walk(library, path_map, folders[:len(quarantine)], folders[len(quarantine):]):
        return None
    return library, qrestore.survey(library, path_map, args.workers, args.tags_waiver, args.db), path_map


def _new_file_outside_roots(value: str, host_roots: list[str]) -> Path | None:
    path = Path(value).resolve()
    if path.exists() or not path.parent.is_dir() or any(is_within(str(path), r) for r in host_roots):
        log.error("%s must be a new file, in an existing folder, outside every library root", value)
        return None
    return path


def _restore_explain(args: argparse.Namespace) -> int:
    if args.with_paths and not args.rows_out:
        log.error("--with-paths needs --rows-out: paths never go to stdout")
        return 2
    surveyed = _restore_survey(args)
    if surveyed is None:
        return 2
    library, result, path_map = surveyed
    if args.rows_out:
        rows_out = _new_file_outside_roots(args.rows_out, [path_map.to_host(r) for r in library.roots])
        if rows_out is None:
            return 2
        rows_out.write_text("\n".join(qrestore.book_lines(result, args.with_paths)) + "\n", encoding="utf-8")
    print("\n".join(qrestore.summary_lines(library, result)))
    return 3 if library.walk_errors else 0


def _restore_plan(args: argparse.Namespace) -> int:
    out_dir = Path(args.out).resolve()
    if out_dir.exists() and any(out_dir.iterdir()):
        log.error("output directory %s exists and is not empty", out_dir)
        return 2
    try:
        chosen = _read_book_ids(Path(args.books))
    except (OSError, ValueError) as error:
        log.error("book list unusable: %s", error)
        return 2
    surveyed = _restore_survey(args)
    if surveyed is None:
        return 2
    library, result, path_map = surveyed
    roots_host = [path_map.to_host(r) for r in library.roots]
    if any(is_within(str(out_dir), r) for r in roots_host):
        log.error("output directory lies inside a library root")
        return 2
    if library.walk_errors:
        log.error("%d walk errors: no restore plan is written on incomplete evidence", len(library.walk_errors))
        return 3
    try:
        plan = qrestore.build_restore_plan(result, chosen, library)
    except ValueError as error:
        log.error("%s", error)
        return 2
    plan["roots_host"] = roots_host
    out_dir.mkdir(parents=True, exist_ok=True)
    path, digest = qrestore.write_restore_plan(plan, str(out_dir))
    log.info("restore plan=%s sha256=%s books=%d files=%d", path, digest, plan["summary"]["books"],
             plan["summary"]["files"])
    print(digest)
    return 0


def _read_book_ids(path: Path) -> frozenset[int]:
    """Audiobook ids, one per line (blank lines and ``#`` comments ignored); ValueError otherwise or when empty."""
    ids: set[int] = set()
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        text = line.strip()
        if not text or text.startswith("#"):
            continue
        if not text.isdecimal():
            raise ValueError(f"{path}:{number}: not an audiobook id: {text!r}")
        ids.add(int(text))
    if not ids:
        raise ValueError(f"{path} names no audiobook id")
    return frozenset(ids)


def _restore_move(args: argparse.Namespace) -> int:
    plan_path = Path(args.plan).resolve()
    if file_sha256(plan_path) != args.plan_sha256.lower():
        log.error("restore plan sha256 does not match the reviewed value")
        return 2
    if not args.dry_run and not args.confirm_service_stopped:
        log.error("restore-move needs --confirm-service-stopped (or --dry-run): stop Listenarr first")
        return 2
    plan = json.loads(plan_path.read_text(encoding="utf-8"))
    roots_host = [str(r) for r in plan.get("roots_host", [])]
    journal = None
    if not args.dry_run:
        journal = _new_file_outside_roots(args.journal or "", roots_host) if args.journal else None
        if journal is None:
            log.error("restore-move needs --journal: a new file outside every root")
            return 2
    try:
        done = qrestore.move_plan(plan, roots_host, str(journal) if journal else None, args.dry_run)
    except qrestore.MoveRefused as error:
        log.error("refused, nothing moved: %s", error)
        return 3
    except OSError as error:
        log.error("stopped part-way, do not start the service: %s; run restore-undo on the journal", error)
        return 4
    print("\n".join(f"{key} {value}" for key, value in sorted(done.items())))
    return 0


def _restore_undo(args: argparse.Namespace) -> int:
    if not args.dry_run and not args.confirm_service_stopped:
        log.error("restore-undo needs --confirm-service-stopped (or --dry-run): stop Listenarr first")
        return 2
    undo = None
    if not args.dry_run:
        undo = Path(args.undo_journal).resolve() if args.undo_journal else None
        if undo is None or undo.exists() or not undo.parent.is_dir():
            log.error("restore-undo needs --undo-journal: a new file in an existing folder")
            return 2
    counts = qrestore.undo_journal(args.journal, str(undo) if undo else None, args.dry_run)
    print("\n".join(f"{key} {value}" for key, value in sorted(counts.items())))
    return 3 if counts["refused"] else 0


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
        # Counts-only output: an OSError's text names its file, so only its description is logged.
        log.error("cannot explain the plan's vetoes: %s: %s", type(error).__name__,
                  error.strerror if isinstance(error, OSError) else error)
        return 2
    print("\n".join(lines))
    return 0


def _add_narrowed_rules(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--tags-waiver", choices=matcher.TAGS_WAIVER_SCOPES, default="none",
                        help="waive 'tags name another library book' for a physical identity match whose file "
                             "handle and folder Asin agree: none (default), same-series (the tags name a book "
                             "sharing a series with this one) or all")
    parser.add_argument("--quarantine-dir", nargs="+", action="extend", metavar="DIR",
                        help="folder(s) outside every library root holding quarantine hard links (no default, "
                             "taken literally). A hard-linked file is matched only when every other name lies "
                             "under one of these, on its dataset, in a folder with this book's bracketed id")


def _add_restore_commands(sub: argparse._SubParsersAction[argparse.ArgumentParser]) -> None:
    def survey_arguments(command: argparse.ArgumentParser) -> None:
        command.add_argument("--db", required=True, help="a COPY of the Listenarr DB (opened read-only)")
        command.add_argument("--path-map", action="append", default=[], metavar="DBPREFIX=HOSTPREFIX")
        command.add_argument("--workers", type=int, default=4, help="parallel ffprobe processes (default 4)")
        command.add_argument("--restore-from", nargs="+", action="extend", required=True, metavar="DIR",
                             help="quarantine folder(s) outside every root whose files may go back (taken literally)")
        _add_narrowed_rules(command)

    explain = sub.add_parser("restore-explain", help="read-only, counts only: which quarantined books may go back")
    survey_arguments(explain)
    explain.add_argument("--rows-out", metavar="FILE", help="one line per candidate book (ids and classes) here")
    explain.add_argument("--with-paths", action="store_true", help="add the target folder and reason to --rows-out")
    explain.set_defaults(handler=_restore_explain)

    plan = sub.add_parser("restore-plan", help="read-only: write the move plan for the operator's chosen books")
    survey_arguments(plan)
    plan.add_argument("--books", required=True, metavar="FILE",
                      help="audiobook ids to restore, one per line; each must be in the restorable class")
    plan.add_argument("--out", required=True, help="new, empty output directory outside every library root")
    plan.set_defaults(handler=_restore_plan)

    move = sub.add_parser("restore-move", help="move a reviewed plan's files back under the root (link, then unlink)")
    move.add_argument("--plan", required=True)
    move.add_argument("--plan-sha256", required=True)
    move.add_argument("--journal", help="new file outside every root: every step is recorded here first")
    move.add_argument("--dry-run", action="store_true", help="check every precondition, move nothing")
    move.add_argument("--confirm-service-stopped", action="store_true")
    move.set_defaults(handler=_restore_move)

    undo = sub.add_parser("restore-undo", help="reverse a restore-move journal, newest step first")
    undo.add_argument("--journal", required=True)
    undo.add_argument("--undo-journal", help="new file: every undo step is recorded here first")
    undo.add_argument("--dry-run", action="store_true", help="say what would be undone, change nothing")
    undo.add_argument("--confirm-service-stopped", action="store_true")
    undo.set_defaults(handler=_restore_undo)


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
    _add_narrowed_rules(plan)
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
                                                  "library book' vetoes (never prints titles or paths; "
                                                  "SQLite may touch the snapshot's -shm file)")
    explain.add_argument("--plan", required=True, help="plan.json produced by `plan` (its db-copy.sqlite is read)")
    explain.add_argument("--path-map", action="append", default=[], metavar="DBPREFIX=HOSTPREFIX",
                         help="default: the plan's own path map")
    explain.add_argument("--workers", type=int, default=4, help="parallel ffprobe processes (default 4)")
    explain.set_defaults(handler=_explain_veto)

    why = sub.add_parser("explain-identity", help="read-only, counts only: classify every stale row's physical "
                                                  "identity against the live files (no plan, nothing written but "
                                                  "--rows-out; SQLite may touch the database's -shm file)")
    why.add_argument("--db", required=True, help="a COPY of the Listenarr DB (opened read-only, never snapshotted)")
    why.add_argument("--path-map", action="append", default=[], metavar="DBPREFIX=HOSTPREFIX",
                     help="translate stored paths to this host's view (repeatable)")
    why.add_argument("--workers", type=int, default=4, help="parallel ffprobe processes (default 4)")
    why.add_argument("--rows-out", metavar="FILE", help="also write one line per stale row here (a new file outside "
                                                       "every library root); ids and classes only")
    why.add_argument("--with-paths", action="store_true", help="add the stored and matched paths and the reason "
                                                               "to --rows-out (that file then stays on this host)")
    _add_narrowed_rules(why)
    why.set_defaults(handler=_explain_identity)

    _add_restore_commands(sub)

    args = parser.parse_args(argv)
    if getattr(args, "workers", 1) < 1:
        parser.error("--workers must be at least 1")
    handler = args.handler
    result: int = handler(args)
    return result
