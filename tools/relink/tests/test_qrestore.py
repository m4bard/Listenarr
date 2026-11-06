"""Quarantine restore end to end on a throwaway library of real files: survey, move plan, move, undo, re-link.

Each book's files are rendered (ffmpeg), registered with their physical identity, then renamed into a quarantine
folder beside the root (a rename keeps the object, as the operator's move did), so every row is stale and its
object sits outside the root. The book's renamed folder under the root carries its Asin. Titles, authors and
ASINs are corpus values.
"""

from __future__ import annotations

import json
import os
import sqlite3
import subprocess
from pathlib import Path
from typing import Any

import pytest
from test_size_placeholder import JUNGLE, PRINCESS, Library, library, relinked, render, stored_identity

from listenarr_relink import cli, inventory, qrestore
from listenarr_relink.paths import PathMap
from listenarr_relink.plan_io import file_sha256

__all__ = ["library"]  # the fixture, re-exported so pytest finds it here

PRINCESS_ASIN = "B008DFUGCQ"
JUNGLE_ASIN = "B00OQQTXE8"
LITTLE_WOMEN = ("Little Women", "Louisa May Alcott", "B081B7JM9F")
SERIES = "Barsoom"


class Restore:
    """The library plus a bad-merge quarantine and a duplicate quarantine beside its root."""

    def __init__(self, library: Library) -> None:
        self.library = library
        self.bad = library.top / "QUAR-BAD"
        self.dup = library.top / "QUAR-DUP"
        self.bad.mkdir()
        self.dup.mkdir()
        with sqlite3.connect(library.db) as connection:
            connection.execute("ALTER TABLE Audiobooks ADD COLUMN Asin TEXT")
            connection.execute("ALTER TABLE Audiobooks ADD COLUMN Runtime REAL")

    def book(self, book_id: int, book: tuple[str, str], asin: str, parts: int = 2, **overrides: Any) -> list[int]:
        """Add a book, set its Asin and a runtime its files fit (each file is 3 s), and make its target folder."""
        rows = self.library.add_book(book_id, book, parts, **overrides)
        with sqlite3.connect(self.library.db) as connection:
            connection.execute("UPDATE Audiobooks SET Asin = ?, Runtime = ? WHERE Id = ?",
                               (asin, parts * 3 / 60, book_id))
        self.target(book, asin).mkdir(parents=True)
        return rows

    def target(self, book: tuple[str, str], asin: str) -> Path:
        """Where the outside renamer put the book's folder: '<author>/<series>/1 - <title> [<asin>]'."""
        return self.library.root / book[1] / SERIES / f"1 - {book[0]} [{asin}]"

    def quarantine(self, rows: list[int], book: tuple[str, str], asin: str, under: Path | None = None) -> Path:
        """Rename the rows' files into '<quarantine>/<author>/<title> [<asin>]/' (the objects stay)."""
        folder = (under or self.bad) / book[1] / f"{book[0]} [{asin}]"
        folder.mkdir(parents=True, exist_ok=True)
        for row in rows:
            os.rename(self.library.rows[row], folder / self.library.rows[row].name)
            self.library.rows[row] = folder / self.library.rows[row].name
        old_folder = self.library.root / book[1] / book[0]
        if old_folder.exists() and not any(old_folder.iterdir()):
            old_folder.rmdir()
        return folder

    def zero_row_book(self, book_id: int, tag_title: str) -> Path:
        """Add Little Women with no file row; its two files (tagged ``tag_title``) sit in its quarantine folder."""
        title, author, asin = LITTLE_WOMEN
        folder = self.bad / author / f"{title} [{asin}]"
        for part in (1, 2):
            render(folder / f"{title} - 0{part}.mp3", 300 + 10 * part, tag_title)
        with sqlite3.connect(self.library.db) as connection:
            connection.execute("INSERT INTO Audiobooks (Id, Title, Authors, BasePath, Asin, Runtime) VALUES "
                               "(?, ?, ?, ?, ?, ?)", (book_id, title, json.dumps([author]),
                                                      str(self.library.root / author / title), asin, 6 / 60))
        self.target((title, author), asin).mkdir(parents=True)
        return folder

    def retag_with_asin(self, row: int, asin: str) -> None:
        """Replace a row's file with a new recording carrying an ASIN tag, and register that object instead."""
        path = self.library.rows[row]
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=777:duration=3",
                        "-c:a", "libmp3lame", "-b:a", "64k", "-ar", "44100", "-ac", "1", "-metadata",
                        f"title={PRINCESS[0]}", "-metadata", f"asin={asin}", str(path) + ".new.mp3"], check=True)
        os.replace(str(path) + ".new.mp3", path)
        probe = json.loads(subprocess.run(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json",
                                           str(path)], check=True, capture_output=True, text=True).stdout)
        self.library.set_row(row, Size=path.stat().st_size, DurationSeconds=float(probe["format"]["duration"]),
                             PhysicalObjectIdentity=stored_identity(path))

    def set_book(self, book_id: int, **values: Any) -> None:
        """Overwrite columns of one Audiobooks row."""
        with sqlite3.connect(self.library.db) as connection:
            connection.execute(f"UPDATE Audiobooks SET {', '.join(f'{k} = ?' for k in values)} WHERE Id = ?",
                               [*values.values(), book_id])

    def survey_args(self, *extra: str) -> list[str]:
        """The arguments every restore command shares."""
        return ["--db", str(self.library.db), "--workers", "2", "--restore-from", str(self.bad),
                "--quarantine-dir", str(self.dup), *extra]

    def explain(self, capsys: pytest.CaptureFixture[str], *extra: str) -> dict[str, str]:
        """Run restore-explain; return its counts, keyed by the line's name."""
        capsys.readouterr()
        assert cli.main(["restore-explain", *self.survey_args(*extra)]) == 0
        lines = capsys.readouterr().out.splitlines()
        return {line.strip().rsplit(" ", 1)[0]: line.strip().rsplit(" ", 1)[1] for line in lines if " " in line}

    def classes(self, *extra: str) -> dict[int, str]:
        """Run restore-explain with --rows-out; return book id -> class."""
        rows_out = self.library.top / f"books-{len(list(self.library.top.glob('books-*')))}.tsv"
        assert cli.main(["restore-explain", *self.survey_args(*extra), "--rows-out", str(rows_out)]) == 0
        lines = rows_out.read_text().splitlines()[1:]
        return {int(cells[0]): cells[2] for cells in (line.split("\t") for line in lines)}

    def plan(self, books: list[int], name: str = "rplan", *extra: str) -> tuple[int, Path]:
        """Run restore-plan for ``books``; return the exit code and the plan folder."""
        chosen = self.library.top / f"{name}-books.txt"
        chosen.write_text("".join(f"{b}\n" for b in books))
        out = self.library.top / name
        return cli.main(["restore-plan", *self.survey_args(*extra), "--books", str(chosen), "--out", str(out)]), out

    def move(self, out: Path, *extra: str, journal: str = "journal.tsv") -> int:
        """Run restore-move on a written plan (service-stopped confirmed)."""
        plan = out / "restore-plan.json"
        return cli.main(["restore-move", "--plan", str(plan), "--plan-sha256", _sha(plan), "--journal",
                         str(self.library.top / journal), "--confirm-service-stopped", *extra])


def _row(library: Library, row: int) -> dict[str, Any]:
    with sqlite3.connect(library.db) as connection:
        connection.row_factory = sqlite3.Row
        return dict(connection.execute("SELECT * FROM AudiobookFiles WHERE Id = ?", (row,)).fetchone())


def long_render(path: Path, title: str, seconds: int) -> None:
    """Write a ``seconds`` long MP3 tagged ``title``."""
    path.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", f"sine=frequency=500:duration={seconds}",
                    "-c:a", "libmp3lame", "-b:a", "32k", "-ar", "22050", "-ac", "1", "-metadata", f"title={title}",
                    "-metadata", f"album={title}", str(path)], check=True)


def _sha(path: Path) -> str:
    return file_sha256(path)


@pytest.fixture
def restore(library: Library) -> Restore:
    """A library with two empty quarantine folders beside its root."""
    return Restore(library)


def test_a_restorable_book_moves_back_and_then_relinks(restore: Restore, capsys: pytest.CaptureFixture[str]) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    folder = restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    (folder / "cover.jpg").write_bytes(b"not audio")  # sidecars stay where they are
    inodes = {row: restore.library.rows[row].stat().st_ino for row in rows}
    # Control: without the move, PLAN finds nothing to re-link (the objects lie outside the root).
    rc, before = restore.library.plan("plan-before")
    assert rc == 0 and before is not None and relinked(before) == {}
    counts = restore.explain(capsys)
    assert counts["restorable"] == "1" and counts["restorable_rows"] == "2" and counts["runtime_fits"] == "1"
    rc, out = restore.plan([1])
    assert rc == 0
    plan = json.loads((out / "restore-plan.json").read_text())
    assert plan["summary"] == {"books": 1, "files": 2, "rows": 2, "zero_row_books": 0}
    assert restore.move(out, "--dry-run") == 0
    assert all(restore.library.rows[row].exists() for row in rows)  # a dry run moves nothing
    assert restore.move(out) == 0
    target = restore.target(PRINCESS, PRINCESS_ASIN)
    moved = {row: target / restore.library.rows[row].name for row in rows}
    assert all(not restore.library.rows[row].exists() for row in rows)
    assert {row: path.stat().st_ino for row, path in moved.items()} == inodes  # the same objects
    assert all(path.stat().st_nlink == 1 for path in moved.values())
    rc, after = restore.library.plan("plan-after")
    assert rc == 0 and after is not None and relinked(after) == {row: str(path) for row, path in moved.items()}
    assert all(entry["confidence"] == "identity" for entry in after["relinks"])


def test_counts_carry_no_title_and_no_path(restore: Restore, capsys: pytest.CaptureFixture[str]) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    capsys.readouterr()
    rows_out = restore.library.top / "books.tsv"
    assert cli.main(["restore-explain", *restore.survey_args(), "--rows-out", str(rows_out)]) == 0
    out = capsys.readouterr().out
    assert str(restore.library.top) not in out and PRINCESS[0] not in out and PRINCESS_ASIN not in out
    assert str(restore.library.top) not in rows_out.read_text() and PRINCESS[0] not in rows_out.read_text()
    assert rows_out.read_text().splitlines()[1].split("\t") == ["1", "rows", "restorable", "2", "2", "fits", "-", "N"]


def test_undo_puts_every_file_back(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    quarantined = {row: restore.library.rows[row] for row in rows}
    _, out = restore.plan([1])
    assert restore.move(out) == 0
    journal = restore.library.top / "journal.tsv"
    undo = restore.library.top / "undo.tsv"
    assert cli.main(["restore-undo", "--journal", str(journal), "--dry-run"]) == 0
    assert not any(path.exists() for path in quarantined.values())  # a dry run undoes nothing
    assert cli.main(["restore-undo", "--journal", str(journal), "--undo-journal", str(undo),
                     "--confirm-service-stopped"]) == 0
    assert all(path.exists() and path.stat().st_nlink == 1 for path in quarantined.values())
    assert not list(restore.target(PRINCESS, PRINCESS_ASIN).iterdir())


def test_undo_finishes_a_move_interrupted_between_link_and_unlink(restore: Restore) -> None:
    (row,) = restore.book(1, PRINCESS, PRINCESS_ASIN, parts=1)
    restore.quarantine([row], PRINCESS, PRINCESS_ASIN)
    source = restore.library.rows[row]
    destination = restore.target(PRINCESS, PRINCESS_ASIN) / source.name
    os.link(source, destination)  # the move stopped after its link
    info = source.stat()
    journal = restore.library.top / "journal.tsv"
    journal.write_text(f"intent\t{info.st_dev}\t{info.st_ino}\t{source}\t{destination}\n")
    assert cli.main(["restore-undo", "--journal", str(journal), "--undo-journal", str(restore.library.top / "u.tsv"),
                     "--confirm-service-stopped"]) == 0
    assert source.exists() and not destination.exists() and source.stat().st_nlink == 1


def test_undo_leaves_a_name_that_is_another_object(restore: Restore) -> None:
    (row,) = restore.book(1, PRINCESS, PRINCESS_ASIN, parts=1)
    restore.quarantine([row], PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])
    assert restore.move(out) == 0
    destination = restore.target(PRINCESS, PRINCESS_ASIN) / restore.library.rows[row].name
    destination.unlink()
    render(destination, 900, "A Princess of Mars")  # another object now holds the restored name
    assert cli.main(["restore-undo", "--journal", str(restore.library.top / "journal.tsv"), "--undo-journal",
                     str(restore.library.top / "u.tsv"), "--confirm-service-stopped"]) == 3
    assert destination.exists() and not restore.library.rows[row].exists()


def test_undo_leaves_a_file_whose_quarantine_folder_is_gone(restore: Restore) -> None:
    (row,) = restore.book(1, PRINCESS, PRINCESS_ASIN, parts=1)
    folder = restore.quarantine([row], PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])
    assert restore.move(out) == 0
    folder.rmdir()
    assert cli.main(["restore-undo", "--journal", str(restore.library.top / "journal.tsv"), "--undo-journal",
                     str(restore.library.top / "u.tsv"), "--confirm-service-stopped"]) == 3
    assert (restore.target(PRINCESS, PRINCESS_ASIN) / restore.library.rows[row].name).exists()


def test_a_book_missing_one_row_object_is_not_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    restore.library.rows[rows[1]].unlink()
    assert restore.classes() == {1: "not-every-row-found"}


def test_a_row_whose_audio_facts_disagree_is_not_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    restore.library.set_row(rows[0], DurationSeconds=999.0)
    assert restore.classes() == {1: "row-content-disagrees"}


def test_tags_naming_another_book_refuse_unless_waived(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN, album="The Jungle Book")
    restore.library.add_book(2, JUNGLE, 1)  # the other library book, live under the root
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes()[1] == "tags-name-another-book"
    assert restore.classes("--tags-waiver", "all")[1] == "restorable"


def test_tags_naming_another_book_are_never_waived_without_a_row(restore: Restore) -> None:
    restore.library.add_book(2, JUNGLE, 1)
    zero = restore.zero_row_book(3, "The Jungle Book")
    assert restore.classes("--tags-waiver", "all")[3] == "tags-name-another-book"
    assert zero.exists()


def test_an_asin_tag_naming_another_edition_is_not_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.retag_with_asin(rows[0], JUNGLE_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes() == {1: "asin-tag-not-own"}


def test_an_asin_tag_naming_this_book_is_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.retag_with_asin(rows[0], PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes() == {1: "restorable"}


def test_a_quarantine_folder_naming_another_asin_is_not_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, JUNGLE_ASIN)
    assert restore.classes() == {1: "source-folder-id-not-asin"}


def test_a_second_copy_merged_in_beside_the_files_does_not_fit_the_runtime(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    folder = restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    long_render(folder / "merged-in.mp3", PRINCESS[0], 120)  # what a bad merge leaves beside the book's own files
    assert restore.classes() == {1: "runtime-misfit"}


def test_a_file_whose_row_was_dropped_goes_back_with_its_siblings(restore: Restore,
                                                                  capsys: pytest.CaptureFixture[str]) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN, parts=3)
    folder = restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    with sqlite3.connect(restore.library.db) as connection:
        connection.execute("DELETE FROM AudiobookFiles WHERE Id = ?", (rows[2],))  # the service removed its row
    counts = restore.explain(capsys)
    assert counts["restorable"] == "1" and counts["restorable_files_without_a_row"] == "1"
    assert counts["restorable_books_rows_needing_a_scan_after_relink"] == "1"
    _, out = restore.plan([1])
    assert restore.move(out) == 0
    target = restore.target(PRINCESS, PRINCESS_ASIN)
    assert sorted(p.name for p in target.iterdir()) == sorted(restore.library.rows[r].name for r in rows)
    assert not list(folder.iterdir())
    rc, after = restore.library.plan("plan-after")
    assert rc == 0 and after is not None and set(relinked(after)) == set(rows[:2])


def test_a_sibling_row_already_under_the_root_does_not_hold_the_book_back(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows[:1], PRINCESS, PRINCESS_ASIN)
    target = restore.target(PRINCESS, PRINCESS_ASIN)
    os.rename(restore.library.rows[rows[1]], target / restore.library.rows[rows[1]].name)  # renamed in place
    restore.library.rows[rows[1]] = target / restore.library.rows[rows[1]].name
    assert restore.classes() == {1: "restorable"}
    _, out = restore.plan([1])
    assert restore.move(out) == 0
    rc, after = restore.library.plan("plan-after")
    assert rc == 0 and after is not None and set(relinked(after)) == set(rows)


def test_files_that_do_not_fit_the_catalog_runtime_are_not_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    restore.set_book(1, Runtime=10)
    assert restore.classes() == {1: "runtime-misfit"}
    restore.set_book(1, Runtime=None)
    assert restore.classes() == {1: "restorable"}  # an unknown runtime is reported, not a refusal


def test_without_a_target_folder_nothing_is_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    restore.target(PRINCESS, PRINCESS_ASIN).rmdir()
    assert restore.classes() == {1: "no-target-folder"}


def test_two_target_folders_refuse_unless_basepath_names_one(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    second = restore.library.root / PRINCESS[1] / f"{PRINCESS[0]} [{PRINCESS_ASIN}]"
    second.mkdir()
    assert restore.classes() == {1: "several-target-folders"}
    restore.set_book(1, BasePath=str(second))
    assert restore.classes() == {1: "restorable"}


def test_a_target_already_holding_audio_is_not_restorable(restore: Restore,
                                                           capsys: pytest.CaptureFixture[str]) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    render(restore.target(PRINCESS, PRINCESS_ASIN) / "copy.mp3", 450, PRINCESS[0])  # a second copy of the book
    counts = restore.explain(capsys)
    # The copy fits the catalog runtime: the signature of a duplicate, which restoring would put beside it.
    assert counts["target-holds-other-audio"] == "1" and counts["target_other_audio_runtime_fits"] == "1"
    restore.set_book(1, Runtime=10)
    counts = restore.explain(capsys)
    assert counts["target_other_audio_runtime_misfit"] == "1" and counts["target_other_audio_runtime_fits"] == "0"


def test_a_destination_name_already_taken_is_not_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    (restore.target(PRINCESS, PRINCESS_ASIN) / restore.library.rows[rows[0]].name).mkdir()
    assert restore.classes() == {1: "target-name-taken"}


def test_another_name_outside_the_quarantines_is_not_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    elsewhere = restore.library.top / "elsewhere"
    elsewhere.mkdir()
    os.link(restore.library.rows[rows[0]], elsewhere / "kept.mp3")
    assert restore.classes() == {1: "hardlinked-elsewhere"}


def test_another_name_in_the_duplicate_quarantine_of_this_book_is_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    dup = restore.dup / PRINCESS[1] / f"{PRINCESS[0]} [{PRINCESS_ASIN}]"
    dup.mkdir(parents=True)
    os.link(restore.library.rows[rows[0]], dup / "dup.mp3")
    assert restore.classes() == {1: "restorable"}
    os.rename(dup, dup.with_name(f"{PRINCESS[0]} [{JUNGLE_ASIN}]"))  # the same name, under another book's id
    assert restore.classes() == {1: "hardlinked-elsewhere"}


def test_a_file_on_another_device_is_not_restorable(restore: Restore, monkeypatch: pytest.MonkeyPatch) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    target = str(restore.target(PRINCESS, PRINCESS_ASIN))
    connection = inventory.open_read_only(str(restore.library.db))
    try:
        library = inventory.Library(*inventory.load_rows(connection))
    finally:
        connection.close()
    inventory.walk_restore_source(str(restore.bad), library)
    book = qrestore.RestoreBook(library.books[1], "rows", target_host=target, target=target)
    book.files = [qrestore.RestoreFile(f, "", None) for f in library.restore_source]
    real_lstat = os.lstat

    def lstat(path: Any, *args: Any, **kwargs: Any) -> os.stat_result:
        info = real_lstat(path, *args, **kwargs)
        if str(path) != target:
            return info
        values = list(info)
        values[2] += 1  # st_dev: the target folder reports another device than the quarantined files
        return os.stat_result(values)

    qrestore._place(book, library, PathMap(), [str(restore.library.root)], {}, PRINCESS_ASIN.casefold())
    assert book.verdict == "restorable"  # control: the same call without the device change
    monkeypatch.setattr(os, "lstat", lstat)
    qrestore._place(book, library, PathMap(), [str(restore.library.root)], {}, PRINCESS_ASIN.casefold())
    assert book.verdict == "cross-device"


def test_a_sibling_under_the_root_linked_into_the_restore_folder_counts_as_planned(
        restore: Restore, capsys: pytest.CaptureFixture[str]) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    folder = restore.quarantine(rows[:1], PRINCESS, PRINCESS_ASIN)
    target = restore.target(PRINCESS, PRINCESS_ASIN)
    os.rename(restore.library.rows[rows[1]], target / restore.library.rows[rows[1]].name)
    os.link(target / restore.library.rows[rows[1]].name, folder / "second-name.mp3")  # a quarantine copy, linked
    assert restore.classes() == {1: "restorable"}
    counts = restore.explain(capsys)
    # Only the quarantined row's file goes back; the sibling's quarantine name stays, its in-root name is PLAN's.
    assert counts["restorable_files"] == "1" and counts["object-under-a-root-plan-links"] == "1"


def test_a_sibling_row_plan_would_refuse_under_the_root_holds_the_book_back(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows[:1], PRINCESS, PRINCESS_ASIN)
    target = restore.target(PRINCESS, PRINCESS_ASIN)
    os.rename(restore.library.rows[rows[1]], target / restore.library.rows[rows[1]].name)
    restore.library.set_row(rows[1], DurationSeconds=999.0)  # PLAN vetoes this sibling: its audio facts disagree
    assert restore.classes() == {1: "not-every-row-found"}


def test_a_file_two_books_would_take_back_refuses_both(restore: Restore) -> None:
    (row,) = restore.book(1, PRINCESS, PRINCESS_ASIN, parts=1)
    restore.quarantine([row], PRINCESS, PRINCESS_ASIN)
    (row2,) = restore.book(2, ("The Gods of Mars", "Edgar Rice Burroughs"), "B01BKS3DPE", parts=1)
    restore.library.rows[row2].unlink()  # its file is gone; its row now carries the first book's object
    carried = {k: v for k, v in _row(restore.library, row).items() if k not in ("Id", "AudiobookId", "Path")}
    restore.library.set_row(row2, **carried)
    assert restore.classes() == {1: "object-shared", 2: "object-shared"}


def test_a_zero_row_book_is_restored_from_its_only_folder(restore: Restore) -> None:
    folder = restore.zero_row_book(3, LITTLE_WOMEN[0])
    files = sorted(p.name for p in folder.iterdir())
    assert restore.classes() == {3: "restorable"}
    rc, out = restore.plan([3])
    assert rc == 0 and json.loads((out / "restore-plan.json").read_text())["summary"]["zero_row_books"] == 1
    assert restore.move(out) == 0
    assert sorted(p.name for p in restore.target(LITTLE_WOMEN[:2], LITTLE_WOMEN[2]).iterdir()) == files


def test_a_zero_row_book_in_two_quarantine_folders_is_not_restorable(restore: Restore) -> None:
    restore.zero_row_book(3, LITTLE_WOMEN[0])
    second = restore.bad / "again" / f"{LITTLE_WOMEN[0]} [{LITTLE_WOMEN[2]}]"
    render(second / "01.mp3", 333, LITTLE_WOMEN[0])
    assert restore.classes() == {3: "several-source-folders"}


def test_restore_plan_refuses_a_book_that_is_not_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, JUNGLE_ASIN)
    rc, out = restore.plan([1])
    assert rc == 2 and not out.exists()
    rows2 = restore.book(2, ("The Gods of Mars", "Edgar Rice Burroughs"), "B01BKS3DPE")
    restore.quarantine(rows2, ("The Gods of Mars", "Edgar Rice Burroughs"), "B01BKS3DPE")
    rc, out = restore.plan([2, 99], "rplan-unknown")
    assert rc == 2 and not out.exists()  # 99 is no candidate


def test_restore_plan_refuses_a_restore_folder_inside_the_root(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    inside = restore.library.root / "QUAR"
    inside.mkdir()
    assert cli.main(["restore-explain", "--db", str(restore.library.db), "--restore-from", str(inside)]) == 2
    assert cli.main(["restore-explain", "--db", str(restore.library.db), "--restore-from", str(restore.bad),
                     "--quarantine-dir", str(restore.bad)]) == 2  # one folder given twice
    missing = restore.library.top / "no-such-quarantine"
    assert cli.main(["restore-explain", "--db", str(restore.library.db), "--restore-from", str(missing)]) == 2


def test_a_move_refuses_everything_when_one_source_changed(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    rows2 = restore.book(2, ("The Gods of Mars", "Edgar Rice Burroughs"), "B01BKS3DPE")
    restore.quarantine(rows2, ("The Gods of Mars", "Edgar Rice Burroughs"), "B01BKS3DPE")
    _, out = restore.plan([1, 2])
    os.utime(restore.library.rows[rows2[1]], ns=(1, 1))
    assert restore.move(out) == 3
    assert all(restore.library.rows[row].exists() for row in [*rows, *rows2])  # nothing moved, not even book 1
    assert not (restore.library.top / "journal.tsv").exists()


def test_a_move_refuses_when_a_destination_appeared(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])
    (restore.target(PRINCESS, PRINCESS_ASIN) / "notes.txt").write_text("not audio")  # the folder may gain sidecars
    (restore.target(PRINCESS, PRINCESS_ASIN) / restore.library.rows[rows[0]].name).mkdir()
    assert restore.move(out) == 3
    assert all(restore.library.rows[row].exists() for row in rows)


def test_a_move_refuses_when_the_target_gained_audio(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])
    render(restore.target(PRINCESS, PRINCESS_ASIN) / "late.mp3", 555, PRINCESS[0])
    assert restore.move(out) == 3


def test_a_move_needs_the_reviewed_sha_the_stop_and_a_new_journal(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])
    plan = str(out / "restore-plan.json")
    journal = str(restore.library.top / "j.tsv")
    assert cli.main(["restore-move", "--plan", plan, "--plan-sha256", "0" * 64, "--journal", journal,
                     "--confirm-service-stopped"]) == 2
    assert cli.main(["restore-move", "--plan", plan, "--plan-sha256", _sha(Path(plan)), "--journal", journal]) == 2
    Path(journal).write_text("")
    assert cli.main(["restore-move", "--plan", plan, "--plan-sha256", _sha(Path(plan)), "--journal", journal,
                     "--confirm-service-stopped"]) == 2
    assert all(restore.library.rows[row].exists() for row in rows)


def _retarget(out: Path, change: Any) -> None:
    plan_file = out / "restore-plan.json"
    plan = json.loads(plan_file.read_text())
    change(plan["books"][0])
    plan_file.write_text(json.dumps(plan))


def test_a_move_refuses_a_plan_whose_names_leave_their_folders(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    live = restore.library.root / "live.mp3"
    render(live, 222, PRINCESS[0])
    _, out = restore.plan([1])
    pristine = (out / "restore-plan.json").read_text()

    def source_under_the_root(book: dict[str, Any]) -> None:
        info = live.stat()
        book["files"][0]["source"] = str(live)
        book["files"][0]["pin"] = {"dev": info.st_dev, "ino": info.st_ino, "size": info.st_size,
                                   "mtime_ns": info.st_mtime_ns, "ctime_ns": info.st_ctime_ns, "nlink": info.st_nlink}

    def destination_outside_the_target(book: dict[str, Any]) -> None:
        book["files"][0]["destination"] = str(restore.library.root / "elsewhere.mp3")

    for change in (source_under_the_root, destination_outside_the_target):
        (out / "restore-plan.json").write_text(pristine)
        _retarget(out, change)
        assert restore.move(out, journal=f"j-{change.__name__}.tsv") == 3
        assert live.exists() and all(restore.library.rows[row].exists() for row in rows)
    (out / "restore-plan.json").write_text(pristine)
    assert restore.move(out) == 0  # control: the plan as written moves


def test_a_move_refuses_a_target_on_another_device(restore: Restore, monkeypatch: pytest.MonkeyPatch) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])
    target = str(restore.target(PRINCESS, PRINCESS_ASIN))
    real = qrestore.lstat_or_none

    def lstat_or_none(path: str) -> os.stat_result | None:
        info = real(path)
        if info is None or path != target:
            return info
        values = list(info)
        values[2] += 1
        return os.stat_result(values)

    monkeypatch.setattr(qrestore, "lstat_or_none", lstat_or_none)
    assert restore.move(out) == 3
    assert all(restore.library.rows[row].exists() for row in rows)


def test_a_move_stops_when_the_new_name_is_not_the_object(restore: Restore, monkeypatch: pytest.MonkeyPatch) -> None:
    (row,) = restore.book(1, PRINCESS, PRINCESS_ASIN, parts=1)
    restore.quarantine([row], PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])

    def copy_instead(source: str, destination: str, *, follow_symlinks: bool = True) -> None:
        Path(destination).write_bytes(Path(source).read_bytes())  # a new object, not a second name

    monkeypatch.setattr(qrestore.os, "link", copy_instead)
    assert restore.move(out) == 4
    assert restore.library.rows[row].exists()  # the source is never unlinked
    journal = (restore.library.top / "journal.tsv").read_text().splitlines()
    assert [line.split("\t")[0] for line in journal] == ["intent"]


def test_a_restore_folder_already_walked_refuses(restore: Restore) -> None:
    library = inventory.Library([str(restore.library.root)], {}, [])
    inventory.walk_root(str(restore.library.root), PathMap(), library)
    with pytest.raises(inventory.QuarantineAliasError):
        inventory.walk_restore_source(str(restore.library.root), library)


def test_a_nested_part_folder_is_recreated_and_undone(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    folder = restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    (folder / "Disc 2").mkdir()
    nested = folder / "Disc 2" / restore.library.rows[rows[1]].name
    os.rename(restore.library.rows[rows[1]], nested)
    restore.library.rows[rows[1]] = nested
    _, out = restore.plan([1])
    assert restore.move(out) == 0
    target = restore.target(PRINCESS, PRINCESS_ASIN)
    assert (target / "Disc 2" / nested.name).exists()
    assert cli.main(["restore-undo", "--journal", str(restore.library.top / "journal.tsv"), "--undo-journal",
                     str(restore.library.top / "u.tsv"), "--confirm-service-stopped"]) == 0
    assert nested.exists() and not (target / "Disc 2").exists()
