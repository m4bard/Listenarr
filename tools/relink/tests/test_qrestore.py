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
from test_size_placeholder import JUNGLE, PELLUCIDAR, PRINCESS, Library, library, relinked, render, stored_identity

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
        self.labels: dict[str, str] = {}
        self.unlabelled: set[str] = set()
        self.extra_label_lines: list[str] = []
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

    def retag(self, row: int, title: str, asin: str | None = None) -> None:
        """Replace a row's file with a new recording tagged ``title`` (title and album) and, if given, an ASIN tag;
        register that object instead (what a wrong-content import, or a tagged release, leaves)."""
        path = self.library.rows[row]
        extra = ["-metadata", f"asin={asin}"] if asin else []
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=777:duration=3",
                        "-c:a", "libmp3lame", "-b:a", "64k", "-ar", "44100", "-ac", "1", "-metadata",
                        f"title={title}", "-metadata", f"album={title}", *extra, str(path) + ".new.mp3"],
                       check=True)
        os.replace(str(path) + ".new.mp3", path)
        probe = json.loads(subprocess.run(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json",
                                           str(path)], check=True, capture_output=True, text=True).stdout)
        self.library.set_row(row, Size=path.stat().st_size, DurationSeconds=float(probe["format"]["duration"]),
                             PhysicalObjectIdentity=stored_identity(path))

    def retag_album(self, row: int, album: str) -> None:
        """Rewrite only the album tag of a row's file in place (the audio packets stay), and re-register it."""
        path = self.library.rows[row]
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(path), "-c", "copy", "-metadata", f"album={album}",
                        str(path) + ".new.mp3"], check=True)
        os.replace(str(path) + ".new.mp3", path)
        self.library.set_row(row, Size=path.stat().st_size, PhysicalObjectIdentity=stored_identity(path))

    def set_book(self, book_id: int, **values: Any) -> None:
        """Overwrite columns of one Audiobooks row."""
        with sqlite3.connect(self.library.db) as connection:
            connection.execute(f"UPDATE Audiobooks SET {', '.join(f'{k} = ?' for k in values)} WHERE Id = ?",
                               [*values.values(), book_id])

    def survey_args(self, *extra: str) -> list[str]:
        """The arguments every restore command shares; every top-level quarantine entry is labelled (default
        doubled-merge, or what ``self.labels`` says)."""
        labels = self.library.top / "labels.tsv"
        labels.write_text("".join(f"{entry.name}\t{self.labels.get(entry.name, 'doubled-merge')}\n"
                                  for entry in self.bad.iterdir() if entry.name not in self.unlabelled)
                          + "".join(f"{line}\n" for line in self.extra_label_lines))
        return ["--db", str(self.library.db), "--workers", "2", "--restore-from", str(self.bad),
                "--quarantine-dir", str(self.dup), "--labels", str(labels), *extra]

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
    assert rows_out.read_text().splitlines()[1].split("\t") == ["1", "rows", "restorable", "2", "2", "fits", "-", "N",
                                                             "doubled-merge", "2", "0", "-"]


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


def test_tags_naming_another_book_always_refuse(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN, album="The Jungle Book")
    restore.library.add_book(2, JUNGLE, 1)  # the other library book, live under the root
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes()[1] == "tags-name-another-book"
    with pytest.raises(SystemExit):  # no waiver exists for a restore
        cli.main(["restore-explain", *restore.survey_args("--tags-waiver", "all")])


def test_a_tag_only_containing_a_short_title_does_not_name_that_book(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN, album="Pellucidar chapter 01")
    restore.library.add_book(2, PELLUCIDAR, 1)  # a one-word library title, contained in the tag
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes()[1] == "restorable"


def test_a_tag_that_is_exactly_another_title_names_that_book(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN, album="Pellucidar")
    restore.library.add_book(2, PELLUCIDAR, 1)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes()[1] == "tags-name-another-book"  # control: the full title, normalised, still vetoes
    restore.retag(rows[0], "pellucidar!", None)  # punctuation and case normalise away
    assert restore.classes()[1] == "tags-name-another-book"


GODS = ("The Gods of Mars", "Edgar Rice Burroughs")
TANAR = ("Tanar of Pellucidar", "Edgar Rice Burroughs")
TANAR_ASIN = "B0C6B525PQ"


@pytest.mark.parametrize("title_tag", ["The Gods of Mars (Unabridged) - 01", "Book 2: The Gods of Mars",
                                       "Edgar Rice Burroughs - The Gods of Mars", "The Gods of Mars: A Novel"])
def test_a_foreign_title_with_a_suffix_under_the_own_album_is_vetoed(restore: Restore, title_tag: str) -> None:
    # Another book's audio under a folder-wide retag of the album to this book's title: round 3 vetoed it.
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.library.add_book(2, GODS, 1)
    for row in rows:
        restore.retag(row, title_tag)
        restore.retag_album(row, PRINCESS[0])
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes()[1] == "tags-name-another-book"


def test_a_foreign_title_with_only_a_track_number_is_vetoed(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN, album="The Gods of Mars - 02")  # numbers are not a title
    restore.library.add_book(2, GODS, 1)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes()[1] == "tags-name-another-book"


def test_own_title_containing_a_one_word_library_title_is_not_vetoed(restore: Restore) -> None:
    rows = restore.book(1, TANAR, TANAR_ASIN)  # tags carry "Tanar of Pellucidar", which holds "Pellucidar"
    restore.library.add_book(2, PELLUCIDAR, 1)
    restore.quarantine(rows, TANAR, TANAR_ASIN)
    assert restore.classes()[1] == "restorable"


def test_tags_naming_another_book_refuse_a_book_without_a_row(restore: Restore) -> None:
    restore.library.add_book(2, JUNGLE, 1)
    zero = restore.zero_row_book(3, "The Jungle Book")
    assert restore.classes()[3] == "tags-name-another-book"
    assert zero.exists()


def test_files_tagged_as_a_work_outside_the_library_are_unconfirmed(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    for row in rows:
        restore.retag(row, "Little Men")  # a wrong-content import: a work that is not a library book
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes() == {1: "content-unconfirmed"}


def test_one_file_not_naming_the_book_leaves_it_unconfirmed(restore: Restore,
                                                            capsys: pytest.CaptureFixture[str]) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.retag(rows[1], "Little Men")
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    counts = restore.explain(capsys)
    assert counts["content-unconfirmed"] == "1" and counts["some"] == "1"


def test_a_zero_row_book_tagged_as_another_work_is_unconfirmed(restore: Restore) -> None:
    restore.zero_row_book(3, "Little Men")
    assert restore.classes() == {3: "content-unconfirmed"}


def test_an_own_asin_tag_alone_does_not_confirm_the_content(restore: Restore,
                                                             capsys: pytest.CaptureFixture[str]) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    for row in rows:  # another work's audio, as manual import leaves it: the target book's ASIN written in
        restore.retag(row, "Little Men", PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes() == {1: "content-unconfirmed"}
    counts = restore.explain(capsys)
    assert counts["books_with_asin_only_files"] == "1" and counts["restorable_books_by_evidence title"] == "0"


def test_the_title_in_the_tags_confirms_with_or_without_an_asin_tag(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.retag(rows[0], PRINCESS[0], PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes() == {1: "restorable"}  # control: the title is what confirms


def test_an_asin_tag_naming_another_edition_is_not_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.retag(rows[0], PRINCESS[0], JUNGLE_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes() == {1: "asin-tag-not-own"}


def test_an_asin_tag_naming_this_book_is_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.retag(rows[0], PRINCESS[0], PRINCESS_ASIN)
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
    assert restore.classes() == {1: "runtime-unknown"}  # no catalog runtime confirms nothing
    restore.set_book(1, Runtime=0.1)
    assert restore.classes() == {1: "restorable"}  # control: 6 s against 6 s fits


def test_a_runtime_merely_near_is_not_restorable(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    folder = restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    long_render(folder / "part-3.mp3", PRINCESS[0], 1200)  # 20 min of this book's audio beside the two rows
    restore.set_book(1, Runtime=21.5)  # 1,290 s against about 1,206 s: 6.5 % off, beyond max(60 s, 1 %)
    assert restore.classes() == {1: "runtime-near"}
    restore.set_book(1, Runtime=20.1)
    assert restore.classes() == {1: "restorable"}


def test_an_unknown_runtime_does_not_let_a_merged_in_copy_through(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    folder = restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    long_render(folder / "merged-in.mp3", PRINCESS[0], 120)
    restore.set_book(1, Runtime=None)
    assert restore.classes() == {1: "runtime-unknown"}


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
    restore.survey_args()  # writes the labels file
    common = ["restore-explain", "--db", str(restore.library.db), "--labels", str(restore.library.top / "labels.tsv")]
    assert cli.main([*common, "--restore-from", str(restore.bad)]) == 0  # control
    inside = restore.library.root / "QUAR"
    inside.mkdir()
    assert cli.main([*common, "--restore-from", str(inside)]) == 2
    assert cli.main([*common, "--restore-from", str(restore.bad), "--quarantine-dir", str(restore.bad)]) == 2
    missing = restore.library.top / "no-such-quarantine"
    assert cli.main([*common, "--restore-from", str(missing)]) == 2


def test_a_labels_file_with_an_unknown_label_is_refused(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    args = restore.survey_args()
    labels = restore.library.top / "labels.tsv"
    labels.write_text(labels.read_text().replace("doubled-merge", "Some free text"))
    assert cli.main(["restore-explain", *args]) == 2


def test_a_deliberate_removal_is_listed_for_review_never_restorable(restore: Restore,
                                                                     capsys: pytest.CaptureFixture[str]) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    assert restore.classes() == {1: "restorable"}  # control: a duplicate-kind label
    for label in ("wrong-content", "wrong-asin", "edition-audit", "manual-review", "empty-stub"):
        restore.labels[PRINCESS[1]] = label
        assert restore.classes() == {1: "operator-re-review"}  # passes every check: the operator looks again
    counts = restore.explain(capsys)
    assert counts["empty-stub operator-re-review"] == "1"
    rc, out = restore.plan([1])
    assert rc == 2 and not out.exists()


def test_a_retagged_copy_of_the_same_audio_is_an_in_root_copy(restore: Restore,
                                                                 capsys: pytest.CaptureFixture[str]) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    target = restore.target(PRINCESS, PRINCESS_ASIN)
    for row in rows:  # the library's copy went through manual import, which wrote an ASIN tag: same audio, new size
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(restore.library.rows[row]), "-c", "copy",
                        "-metadata", f"asin={PRINCESS_ASIN}", str(target / f"copy-{restore.library.rows[row].name}")],
                       check=True)
        copy = target / f"copy-{restore.library.rows[row].name}"
        assert copy.stat().st_size != restore.library.rows[row].stat().st_size
    assert restore.classes() == {1: "in-root-copy"}
    restore.set_book(1, Runtime=10)  # a content failure under the copy: counted apart for the re-point design
    counts = restore.explain(capsys)
    assert counts["in_root_copy_books_with_a_content_failure"] == "1"
    restore.set_book(1, Runtime=0.1)
    (target / f"copy-{restore.library.rows[rows[1]].name}").unlink()
    render(target / "other.mp3", 999, PRINCESS[0])  # control: different audio is still not a copy
    assert restore.classes() == {1: "target-holds-other-audio"}


def test_a_deliberate_label_wins_over_an_in_root_copy(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    target = restore.target(PRINCESS, PRINCESS_ASIN)
    for row in rows:
        (target / f"copy-{restore.library.rows[row].name}").write_bytes(restore.library.rows[row].read_bytes())
    restore.labels[PRINCESS[1]] = "wrong-content"  # re-pointing rows at the same wrong audio would keep the mistake
    assert restore.classes() == {1: "deliberate-removal"}


def test_label_audit_counts_entries_rows_and_folders(restore: Restore, capsys: pytest.CaptureFixture[str]) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    restore.zero_row_book(3, LITTLE_WOMEN[0])
    restore.labels["Louisa May Alcott"] = "edition-audit"
    counts = restore.explain(capsys)
    assert counts["label_entries"] == "2" and counts["label_entries_matched"] == "2"
    assert counts["label_lines_unmatched"] == "0"
    assert counts["stale_rows_by_label doubled-merge"] == "2" and counts["stale_rows_by_label edition-audit"] == "0"
    assert counts["folders_with_files_by_label doubled-merge"] == "1"
    assert counts["folders_with_files_by_label edition-audit"] == "1"


def test_restore_plan_refuses_labels_that_do_not_match_the_quarantine(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    rc, out = restore.plan([1], "rplan-control")
    assert rc == 0  # control: every entry labelled, every line used
    restore.extra_label_lines.append("No Such Entry\tdoubled-merge")  # a typo'd or foreign line
    rc, out = restore.plan([1], "rplan-extra")
    assert rc == 2 and not out.exists()
    restore.extra_label_lines.clear()
    rows2 = restore.book(2, ("The Gods of Mars", "Edgar Rice Burroughs"), "B01BKS3DPE")
    restore.quarantine(rows2, ("The Gods of Mars", "Edgar Rice Burroughs"), "B01BKS3DPE",
                       under=restore.bad / "second run")
    restore.unlabelled.add("second run")  # an entry with no label line
    rc, out = restore.plan([1], "rplan-missing")
    assert rc == 2 and not out.exists()


def test_every_failing_check_is_counted_not_only_the_first(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.retag(rows[1], "Little Men")  # content-unconfirmed, a later check than the runtime
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    restore.set_book(1, Runtime=10)  # runtime-misfit, the first failure
    restore.labels[PRINCESS[1]] = "edition-audit"
    out = restore.library.top / "books-check.tsv"
    assert cli.main(["restore-explain", *restore.survey_args(), "--rows-out", str(out)]) == 0
    cells = out.read_text().splitlines()[1].split("\t")
    assert cells[2] == "runtime-misfit"  # the class is still the first failure
    assert cells[-1] == "runtime-misfit,content-unconfirmed,deliberate-label"


def test_failing_and_only_failing_counts(restore: Restore, capsys: pytest.CaptureFixture[str]) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    restore.set_book(1, Runtime=10)
    capsys.readouterr()
    assert cli.main(["restore-explain", *restore.survey_args()]) == 0
    out = capsys.readouterr().out.splitlines()
    failing = out[out.index("failing_checks (books failing each check; a book counts once per check it fails):") + 1:]
    only = out[out.index("books_failing_only (the one check a book fails: what fixing that check alone would free):")
               + 1:]
    assert "  runtime-misfit 1" in failing[:20] and "  runtime-misfit 1" in only[:20]
    restore.retag(rows[1], "Little Men")  # a second failure: still failing, no longer the only one
    assert cli.main(["restore-explain", *restore.survey_args()]) == 0
    out = capsys.readouterr().out.splitlines()
    only = out[out.index("books_failing_only (the one check a book fails: what fixing that check alone would free):")
               + 1:]
    assert "  runtime-misfit 0" in only[:20]


def test_an_unlabelled_entry_fails_closed(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    restore.unlabelled.add(PRINCESS[1])
    assert restore.classes() == {1: "unlabelled"}


def test_a_book_the_library_still_holds_a_copy_of_is_relinked_not_restored(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    target = restore.target(PRINCESS, PRINCESS_ASIN)
    for row in rows:  # a doubled merge: the other copy stayed in the library
        (target / f"copy-{restore.library.rows[row].name}").write_bytes(restore.library.rows[row].read_bytes())
    assert restore.classes() == {1: "in-root-copy"}
    (target / f"copy-{restore.library.rows[rows[1]].name}").unlink()
    render(target / "other.mp3", 999, PRINCESS[0])  # one file differs: not a copy of every file
    assert restore.classes() == {1: "target-holds-other-audio"}


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


def _basepath(restore: Restore, book_id: int) -> str:
    with sqlite3.connect(restore.library.db) as connection:
        return str(connection.execute("SELECT BasePath FROM Audiobooks WHERE Id = ?", (book_id,)).fetchone()[0])


def _restore_basepath(restore: Restore, out: Path, *extra: str) -> int:
    plan = out / "restore-plan.json"
    return cli.main(["restore-basepath", "--db", str(restore.library.db), "--plan", str(plan), "--plan-sha256",
                     _sha(plan), *extra])


def test_restore_basepath_points_a_zero_row_book_at_its_target(restore: Restore) -> None:
    restore.zero_row_book(3, LITTLE_WOMEN[0])
    target = restore.target(LITTLE_WOMEN[:2], LITTLE_WOMEN[2])
    restore.set_book(3, BasePath=str(restore.library.root / "Louisa May Alcott" / "Little Women"))  # gone
    _, out = restore.plan([3])
    plan = json.loads((out / "restore-plan.json").read_text())
    assert plan["books"][0]["basepath_update"]["set"] == str(target)
    before = _basepath(restore, 3)
    assert _restore_basepath(restore, out) == 2  # the stop is not confirmed
    plan_file = str(out / "restore-plan.json")
    assert cli.main(["restore-basepath", "--db", str(restore.library.db), "--plan", plan_file, "--plan-sha256",
                     "0" * 64, "--confirm-service-stopped"]) == 2  # not the reviewed plan
    assert _basepath(restore, 3) == before
    assert _restore_basepath(restore, out, "--dry-run") == 0 and _basepath(restore, 3) == before
    assert _restore_basepath(restore, out, "--confirm-service-stopped") == 0
    assert _basepath(restore, 3) == str(target)
    (undo,) = out.glob("undo-basepath-*.sql")
    with sqlite3.connect(restore.library.db) as connection:
        connection.executescript(undo.read_text())
    assert _basepath(restore, 3) == before


def test_restore_basepath_writes_nothing_when_a_book_changed(restore: Restore) -> None:
    restore.zero_row_book(3, LITTLE_WOMEN[0])
    restore.set_book(3, BasePath=str(restore.library.root / "elsewhere"))
    _, out = restore.plan([3])
    restore.set_book(3, Title="Little Women, retitled")  # the row the plan read has changed
    assert _restore_basepath(restore, out, "--confirm-service-stopped") == 3
    assert _basepath(restore, 3) == str(restore.library.root / "elsewhere")


def test_a_book_whose_basepath_is_its_target_needs_no_update(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    restore.set_book(1, BasePath=str(restore.target(PRINCESS, PRINCESS_ASIN)))
    _, out = restore.plan([1])
    assert json.loads((out / "restore-plan.json").read_text())["books"][0]["basepath_update"] is None


def test_a_name_that_is_not_utf8_is_refused_before_anything_moves(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    folder = restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    os.rename(os.fsencode(restore.library.rows[rows[1]]), os.path.join(os.fsencode(folder), b"part-\xff.mp3"))
    rc, out = restore.plan([1])
    assert rc == 0
    assert restore.move(out) == 3
    assert restore.library.rows[rows[0]].exists()  # nothing moved, not even the first file


def test_any_failure_part_way_exits_4_never_a_traceback(restore: Restore, monkeypatch: pytest.MonkeyPatch) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])
    real = qrestore._move_one
    calls: list[int] = []

    def second_fails(*args: Any) -> None:
        calls.append(1)
        if len(calls) == 2:
            raise ValueError("not an OSError")
        real(*args)

    monkeypatch.setattr(qrestore, "_move_one", second_fails)
    assert restore.move(out) == 4


def test_a_destination_with_a_newline_is_refused(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])
    target = str(restore.target(PRINCESS, PRINCESS_ASIN))
    _retarget(out, lambda book: book["files"][0].update(destination=target + "/two\nlines.mp3"))
    assert restore.move(out) == 3
    assert all(restore.library.rows[row].exists() for row in rows)


def test_two_entries_to_one_destination_are_refused_before_anything_moves(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])

    def same_name(book: dict[str, Any]) -> None:
        book["files"][1]["destination"] = book["files"][0]["destination"].upper().replace(
            book["target_host"].upper(), book["target_host"])

    _retarget(out, same_name)
    assert restore.move(out) == 3
    assert all(restore.library.rows[row].exists() for row in rows)


def test_an_object_a_live_row_holds_is_never_restored_for_a_stale_row(restore: Restore) -> None:
    (live,) = restore.library.add_book(2, ("The Gods of Mars", "Edgar Rice Burroughs"), 1)
    (row,) = restore.book(1, PRINCESS, PRINCESS_ASIN, parts=1)
    folder = restore.quarantine([row], PRINCESS, PRINCESS_ASIN)
    restore.library.rows[row].unlink()
    os.link(restore.library.rows[live], folder / restore.library.rows[row].name)  # the live row's file, linked in
    carried = {k: v for k, v in _row(restore.library, live).items() if k in ("PhysicalObjectIdentity", "Size",
                                                                             "DurationSeconds")}
    restore.library.set_row(row, **carried)  # the stale row claims the live row's object
    assert 1 not in restore.classes()


def test_restore_plan_refuses_on_an_incomplete_walk(restore: Restore, monkeypatch: pytest.MonkeyPatch) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    real = inventory.walk_restore_source

    def unreadable(folder: str, library: inventory.Library) -> None:
        real(folder, library)
        library.walk_errors.append("a folder: Permission denied")

    monkeypatch.setattr(inventory, "walk_restore_source", unreadable)
    rc, out = restore.plan([1])
    assert rc == 3 and not (out / "restore-plan.json").exists()


def test_a_journal_inside_a_root_is_refused(restore: Restore) -> None:
    rows = restore.book(1, PRINCESS, PRINCESS_ASIN)
    restore.quarantine(rows, PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])
    plan = out / "restore-plan.json"
    assert cli.main(["restore-move", "--plan", str(plan), "--plan-sha256", _sha(plan), "--journal",
                     str(restore.library.root / "journal.tsv"), "--confirm-service-stopped"]) == 2
    assert all(restore.library.rows[row].exists() for row in rows)


def test_a_move_stops_when_the_source_name_changed_after_the_link(restore: Restore,
                                                                  monkeypatch: pytest.MonkeyPatch) -> None:
    (row,) = restore.book(1, PRINCESS, PRINCESS_ASIN, parts=1)
    restore.quarantine([row], PRINCESS, PRINCESS_ASIN)
    _, out = restore.plan([1])
    real = os.link

    def link_then_swap_source(source: str, destination: str, *, follow_symlinks: bool = True) -> None:
        real(source, destination, follow_symlinks=follow_symlinks)
        os.unlink(source)
        Path(source).write_bytes(b"another object now holds the quarantine name")

    monkeypatch.setattr(qrestore.os, "link", link_then_swap_source)
    assert restore.move(out) == 4
    assert restore.library.rows[row].read_bytes() == b"another object now holds the quarantine name"  # not unlinked
