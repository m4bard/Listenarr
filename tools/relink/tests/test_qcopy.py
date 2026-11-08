"""Re-pointing an in-root-copy book's stale rows at the library's copy: plan, verify, APPLY, and the refusals.

The quarantined files are real (ffmpeg); each copy in the book's target folder is a retagged `-c copy` of one,
so the audio packets are identical and the size differs, as a copy that went through manual import does.
Titles, authors and ASINs are corpus values.
"""

from __future__ import annotations

import json
import shutil
import sqlite3
import subprocess
from pathlib import Path

import pytest
from test_qrestore import GODS, PRINCESS_ASIN, Restore, restore
from test_size_placeholder import PRINCESS, library, relinked, render, stored_identity

from listenarr_relink import cli
from listenarr_relink.plan_io import file_sha256

__all__ = ["library", "restore"]
GODS_ASIN = "B01BKS3DPE"


def copy_into_target(restore: Restore, rows: list[int], book: tuple[str, str], asin: str) -> dict[int, Path]:
    """Write a retagged copy of each row's quarantined file into the book's target folder."""
    target = restore.target(book, asin)
    copies: dict[int, Path] = {}
    for row in rows:
        copy = target / f"copy-{restore.library.rows[row].name}"
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(restore.library.rows[row]), "-c", "copy",
                        "-metadata", f"asin={asin}", str(copy)], check=True)
        copies[row] = copy
    return copies


def copy_plan(restore: Restore, books: list[int], name: str = "cplan") -> tuple[int, Path]:
    chosen = restore.library.top / f"{name}-books.txt"
    chosen.write_text("".join(f"{b}\n" for b in books))
    out = restore.library.top / name
    return cli.main(["restore-copy-plan", *restore.survey_args(), "--books", str(chosen), "--out", str(out)]), out


def apply(restore: Restore, out: Path) -> int:
    plan = out / "plan.json"
    backup = restore.library.top / f"{out.name}-backup.sqlite"
    shutil.copy(restore.library.db, backup)
    return cli.main(["apply", "--db", str(restore.library.db), "--plan", str(plan), "--plan-sha256",
                     file_sha256(plan), "--backup", str(backup), "--confirm-service-stopped"])


def quarantined_copy_book(restore: Restore) -> tuple[list[int], dict[int, Path]]:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    return rows, copy_into_target(restore, rows, PRINCESS, PRINCESS_ASIN)


def test_an_in_root_copy_book_is_re_pointed_at_its_copy(restore: Restore, capsys: pytest.CaptureFixture[str]) -> None:
    rows, copies = quarantined_copy_book(restore)
    assert restore.classes() == {1: "in-root-copy"}
    rc, out = copy_plan(restore, [1])
    assert rc == 0
    plan = json.loads((out / "plan.json").read_text())
    assert relinked(plan) == {row: str(copy) for row, copy in copies.items()}
    assert all(not e["keep_physical_identity"] for e in plan["relinks"])  # the identity named the original
    assert plan["book_updates"][0]["set"] == {"BasePath": str(restore.target(PRINCESS, PRINCESS_ASIN))}
    capsys.readouterr()
    assert cli.main(["restore-copy-verify", "--plan", str(out / "plan.json")]) == 0
    assert "audio_mismatches 0" in capsys.readouterr().out
    assert apply(restore, out) == 0
    assert restore.library.paths() == {row: str(copy) for row, copy in copies.items()}
    with sqlite3.connect(restore.library.db) as connection:
        assert connection.execute("SELECT count(*) FROM AudiobookFiles WHERE PhysicalObjectIdentity IS NULL"
                                  ).fetchone() == (2,)
        assert connection.execute("SELECT BasePath FROM Audiobooks WHERE Id = 1").fetchone() == (
            str(restore.target(PRINCESS, PRINCESS_ASIN)),)
    assert all(restore.library.rows[row].exists() for row in rows)  # nothing moved: the originals stay put
    rc, after = restore.library.plan("plan-after")
    assert rc == 0 and after is not None and relinked(after) == {}  # the rows are live: nothing left to re-link


def test_a_copy_another_row_already_tracks_is_refused(restore: Restore) -> None:
    rows, copies = quarantined_copy_book(restore)
    restore.library.add_book(2, GODS, 1)
    restore.library.set_row(3, Path=str(copies[rows[0]]))  # another book's row stores the copy's path
    assert restore.classes()[1] == "in-root-copy"  # the class alone does not see it
    rc, out = copy_plan(restore, [1])
    assert rc == 2 and not out.exists()


def test_a_book_that_is_not_in_root_copy_is_refused(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)  # no copy in the target: restorable, not in-root-copy
    assert restore.classes() == {1: "restorable"}
    rc, out = copy_plan(restore, [1])
    assert rc == 2 and not out.exists()


def test_an_in_root_copy_with_a_content_failure_is_refused(restore: Restore) -> None:
    quarantined_copy_book(restore)
    restore.set_book(1, Runtime=10)  # runtime-misfit under the copy
    assert restore.classes() == {1: "in-root-copy"}
    rc, out = copy_plan(restore, [1])
    assert rc == 2 and not out.exists()


def test_a_stale_row_without_a_paired_copy_refuses_the_book(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows[:1], PRINCESS, PRINCESS_ASIN)
    target = restore.target(PRINCESS, PRINCESS_ASIN)
    restore.library.rows[rows[1]].rename(target / restore.library.rows[rows[1]].name)  # left to PLAN, not a copy
    copy_into_target(restore, rows[:1], PRINCESS, PRINCESS_ASIN)
    assert restore.classes() == {1: "in-root-copy"}
    rc, out = copy_plan(restore, [1])
    assert rc == 2 and not out.exists()


def test_one_refused_book_refuses_the_whole_plan(restore: Restore) -> None:
    quarantined_copy_book(restore)
    rows2 = restore.book(2, GODS, GODS_ASIN)
    restore.quarantine(rows2, GODS, GODS_ASIN)  # the control: restorable, not in-root-copy
    rc, out = copy_plan(restore, [1, 2])
    assert rc == 2 and not out.exists()
    rc, out = copy_plan(restore, [1], "cplan-alone")
    assert rc == 0  # control: the good book alone plans


def test_verify_refuses_a_copy_whose_audio_changed(restore: Restore) -> None:
    rows, copies = quarantined_copy_book(restore)
    _, out = copy_plan(restore, [1])
    before = restore.library.paths()
    copies[rows[0]].unlink()
    render(copies[rows[0]], 999, PRINCESS[0])  # other audio at the copy's name
    assert cli.main(["restore-copy-verify", "--plan", str(out / "plan.json")]) == 3
    assert apply(restore, out) == 3  # the pinned object changed too: APPLY refuses and writes nothing
    assert restore.library.paths() == before


def test_a_removed_on_purpose_book_with_a_copy_is_refused(restore: Restore) -> None:
    quarantined_copy_book(restore)
    restore.labels[PRINCESS[1]] = "wrong-content"  # re-pointing at the same wrong audio would keep the mistake
    assert restore.classes() == {1: "deliberate-removal"}
    rc, out = copy_plan(restore, [1])
    assert rc == 2 and not out.exists()


def test_a_copy_that_is_another_books_stale_row_object_is_refused(restore: Restore) -> None:
    rows, copies = quarantined_copy_book(restore)
    restore.library.add_book(2, GODS, 1)
    restore.library.rows[3].unlink()  # book 2's row is stale (its file is gone) ...
    restore.library.set_row(3, PhysicalObjectIdentity=stored_identity(copies[rows[0]]))  # ... and names the copy
    assert restore.classes()[1] == "in-root-copy"  # the class does not see it
    rc, out = copy_plan(restore, [1])
    assert rc == 2 and not out.exists()


def test_a_target_holding_unpaired_audio_is_refused(restore: Restore) -> None:
    quarantined_copy_book(restore)
    render(restore.target(PRINCESS, PRINCESS_ASIN) / "99 - extra.mp3", 777, PRINCESS[0])  # a scan would claim it
    assert restore.classes() == {1: "in-root-copy"}
    rc, out = copy_plan(restore, [1])
    assert rc == 2 and not out.exists()
