"""plan --operator-links on small real libraries: each lift, each refusal, and that a lift does not leak.

Files are rendered with ffmpeg and registered with their physical identity, then renamed (the object stays) into
the folder the leftover class has: another edition's id, the book's own id, and so on. A Princess of Mars has two
corpus ASINs; the second stands for another edition's id. Titles, authors and ASINs are corpus values.
"""

from __future__ import annotations

import json
import os
import sqlite3
from pathlib import Path
from typing import Any

import pytest
from test_size_placeholder import JUNGLE, PRINCESS, Library, library, relinked, render, stored_identity

from listenarr_relink import media
from listenarr_relink.plan_io import file_sha256

__all__ = ["library"]

PRINCESS_ASIN = "B008DFUGCQ"
EDITION_ASIN = "B071YLS9YL"  # the same work's other corpus edition: no library book holds it
JUNGLE_ASIN = "B00OQQTXE8"
GODS = ("The Gods of Mars", "Edgar Rice Burroughs")
GODS_ASIN = "B01BKS3DPE"
Y_ASIN = "B003750OH4"  # Little Men's ASIN, standing for another book's id on a quarantine folder


@pytest.fixture
def lib(library: Library) -> Library:
    with sqlite3.connect(library.db) as connection:
        connection.execute("ALTER TABLE Audiobooks ADD COLUMN Asin TEXT")
        connection.execute("ALTER TABLE Audiobooks ADD COLUMN Runtime REAL")
    return library


def add(lib: Library, book_id: int, book: tuple[str, str], asin: str, parts: int = 1, **overrides: Any) -> list[int]:
    rows = lib.add_book(book_id, book, parts, **overrides)
    with sqlite3.connect(lib.db) as connection:
        connection.execute("UPDATE Audiobooks SET Asin = ?, Runtime = ? WHERE Id = ?", (asin, parts * 3 / 60, book_id))
    return rows


def move_to(lib: Library, row: int, folder: Path) -> Path:
    folder.mkdir(parents=True, exist_ok=True)
    new = folder / lib.rows[row].name
    os.rename(lib.rows[row], new)
    lib.rows[row] = new
    return new


def edition_folder(lib: Library, book: tuple[str, str], asin: str) -> Path:
    return lib.root / book[1] / f"{book[0]} [{asin}]"


def retag(lib: Library, row: int, title: str) -> None:
    """Replace a row's file with another recording tagged ``title`` and register that object (a different work)."""
    path = lib.rows[row]
    facts = render(path.with_name("new-" + path.name), 777, title)
    os.replace(path.with_name("new-" + path.name), path)
    lib.set_row(row, **facts, PhysicalObjectIdentity=stored_identity(path))


def links_file(lib: Library, lines: list[tuple[int, Path | str, str]], name: str = "links.tsv") -> Path:
    path = lib.top / name
    path.write_text("".join(f"{row}\t{target}\t{reason}\n" for row, target, reason in lines))
    return path


def plan(lib: Library, name: str, *extra: str) -> tuple[int, dict[str, Any] | None]:
    quarantine = lib.top / "QUAR"
    quarantine.mkdir(exist_ok=True)
    return lib.plan(name, "--quarantine-dir", str(quarantine), *extra)


def test_folder_edition_lifts_only_the_listed_row(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    target = move_to(lib, row, edition_folder(lib, PRINCESS, EDITION_ASIN))
    (other,) = add(lib, 2, GODS, GODS_ASIN)  # the same shape, not listed
    move_to(lib, other, edition_folder(lib, GODS, EDITION_ASIN))
    rc, before = plan(lib, "without")
    assert rc == 0 and before is not None and relinked(before) == {}  # control: PLAN refuses both
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "folder-edition")])))
    assert rc == 0 and after is not None
    assert relinked(after) == {row: str(target)}  # the unlisted row of the same class stays refused
    assert after["operator_links"]["accepted_rows"] == {str(row): "folder-edition"}
    assert lib.apply("with") == 0 and lib.paths()[row] == str(target)


def test_folder_edition_needs_the_book_to_be_its_own_work(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    retag(lib, row, "Little Men")  # a different work under another edition's id (answers/40's FID48 shape)
    target = move_to(lib, row, edition_folder(lib, PRINCESS, EDITION_ASIN))
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "folder-edition")])))
    assert rc == 0 and after is not None and relinked(after) == {}
    assert "operator_links_refused_books no-own-title-evidence 1" in after["operator_links"]["counts"]


def test_folder_edition_refuses_a_folder_naming_a_library_book(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    add(lib, 2, JUNGLE, JUNGLE_ASIN)
    target = move_to(lib, row, edition_folder(lib, PRINCESS, JUNGLE_ASIN))  # HL9's shape: another library book's id
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "folder-edition")])))
    assert rc == 0 and after is not None and relinked(after) == {}
    assert "operator_links_refused_books folder-names-a-library-book 1" in after["operator_links"]["counts"]


def test_a_book_is_linked_whole_or_not_at_all(lib: Library) -> None:
    rows = add(lib, 1, PRINCESS, PRINCESS_ASIN, parts=2)
    targets = [move_to(lib, row, edition_folder(lib, PRINCESS, EDITION_ASIN)) for row in rows]
    rc, half = plan(lib, "half", "--operator-links", str(links_file(lib, [(rows[0], targets[0], "folder-edition")])))
    assert rc == 0 and half is not None and relinked(half) == {}
    assert "operator_links_refused_books book-not-whole 1" in half["operator_links"]["counts"]
    whole = links_file(lib, [(r, t, "folder-edition") for r, t in zip(rows, targets, strict=True)], "whole.tsv")
    rc, full = plan(lib, "whole", "--operator-links", str(whole))
    assert rc == 0 and full is not None and set(relinked(full)) == set(rows)


def test_a_target_another_row_names_by_identity_is_refused(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    target = move_to(lib, row, edition_folder(lib, PRINCESS, EDITION_ASIN))
    (other,) = add(lib, 2, GODS, GODS_ASIN)
    lib.rows[other].unlink()  # another book's stale row ...
    lib.set_row(other, PhysicalObjectIdentity=stored_identity(target))  # ... names the same object
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "folder-edition")])))
    # PLAN already calls both rows ambiguous, so a link naming this row is a list error: no plan at all.
    assert rc == 2 and after is None


def hardlinked(lib: Library, row: int, in_root: Path, quarantine_folder: str) -> Path:
    target = move_to(lib, row, in_root)
    name = lib.top / "QUAR" / quarantine_folder
    name.mkdir(parents=True, exist_ok=True)
    os.link(target, name / target.name)
    return target


def test_hl_edition_lifts_the_hard_link_and_the_folder_id(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    target = hardlinked(lib, row, edition_folder(lib, PRINCESS, EDITION_ASIN), f"{PRINCESS[0]} [{EDITION_ASIN}]")
    rc, before = plan(lib, "without")
    assert rc == 0 and before is not None and relinked(before) == {}
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "hl-edition")])))
    assert rc == 0 and after is not None and relinked(after) == {row: str(target)}


def test_hl_edition_refuses_a_quarantine_name_under_another_id(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    target = hardlinked(lib, row, edition_folder(lib, PRINCESS, EDITION_ASIN), f"Little Men [{Y_ASIN}]")
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "hl-edition")])))
    assert rc == 0 and after is not None and relinked(after) == {}
    assert "operator_links_refused_books other-names-ambiguous 1" in after["operator_links"]["counts"]


@pytest.mark.parametrize("quarantine_folder", [f"[{Y_ASIN}] - Little Men", f"Little Men [{Y_ASIN}]"])
def test_hl_other_quarantine_accepts_one_other_id_anywhere_in_the_name(lib: Library, quarantine_folder: str) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    target = hardlinked(lib, row, edition_folder(lib, PRINCESS, PRINCESS_ASIN), quarantine_folder)
    rc, before = plan(lib, "without")
    assert rc == 0 and before is not None and relinked(before) == {}
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "hl-other-quarantine")])))
    assert rc == 0 and after is not None and relinked(after) == {row: str(target)}


@pytest.mark.parametrize("case", ["no-id", "two-ids", "nlink-3"])
def test_hl_other_quarantine_refuses_anything_ambiguous(lib: Library, case: str) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    folder = {"no-id": "Little Men", "two-ids": f"[{Y_ASIN}] [{GODS_ASIN}]", "nlink-3": f"[{Y_ASIN}] x"}[case]
    target = hardlinked(lib, row, edition_folder(lib, PRINCESS, PRINCESS_ASIN), folder)
    if case == "nlink-3":
        second = lib.top / "QUAR" / f"[{Y_ASIN}] y"
        second.mkdir()
        os.link(target, second / target.name)
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "hl-other-quarantine")])))
    assert rc == 0 and after is not None and relinked(after) == {}
    assert "operator_links_refused_books other-names-ambiguous 1" in after["operator_links"]["counts"]


def container_says(monkeypatch: pytest.MonkeyPatch, extra_seconds: float) -> None:
    """The container claims a duration ``extra_seconds`` off what decoding gives (answers/40's DUR3 shape)."""
    real = media.probe

    def probe(path: str) -> media.Probe:
        found = real(path)
        return media.Probe((found.duration or 0) + extra_seconds, found.codec, found.sample_rate, found.channels,
                           found.bit_rate, found.tags, found.error)

    monkeypatch.setattr(media, "probe", probe)


def test_duration_decode_lifts_the_duration_check_only_when_decoding_gives_the_stored_value(
        lib: Library, monkeypatch: pytest.MonkeyPatch) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    target = move_to(lib, row, edition_folder(lib, PRINCESS, PRINCESS_ASIN))
    container_says(monkeypatch, 100.0)
    rc, before = plan(lib, "without")
    assert rc == 0 and before is not None and relinked(before) == {}  # control: the duration veto fires
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "duration-decode")])))
    assert rc == 0 and after is not None and relinked(after) == {row: str(target)}


def test_duration_decode_refuses_when_the_stored_duration_is_what_is_wrong(lib: Library,
                                                                          monkeypatch: pytest.MonkeyPatch) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN, DurationSeconds=120.0)  # the stored value is off; decoding says 3 s
    target = move_to(lib, row, edition_folder(lib, PRINCESS, PRINCESS_ASIN))
    container_says(monkeypatch, 200.0)
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "duration-decode")])))
    assert rc == 0 and after is not None and relinked(after) == {}
    assert "operator_links_refused_books decode-differs-from-stored 1" in after["operator_links"]["counts"]


def test_a_malformed_list_refuses_the_run(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    target = move_to(lib, row, edition_folder(lib, PRINCESS, EDITION_ASIN))
    (live,) = add(lib, 2, GODS, GODS_ASIN)  # stays at its path: live
    cases = {
        "unknown-reason": [(row, target, "everything")],
        "row-not-stale": [(live, lib.rows[live], "folder-edition")],
        "reason-of-another-class": [(row, target, "duration-decode")],
        "not-the-own-object": [(row, lib.rows[live], "folder-edition")],
        "row-twice": [(row, target, "folder-edition"), (row, target, "folder-edition")],
    }
    for name, lines in cases.items():
        rc, written = plan(lib, f"bad-{name}", "--operator-links", str(links_file(lib, lines, f"{name}.tsv")))
        assert rc == 2 and written is None, name
    rc, good = plan(lib, "good", "--operator-links", str(links_file(lib, [(row, target, "folder-edition")], "g.tsv")))
    assert rc == 0 and good is not None  # control


def test_the_plan_records_the_list(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    target = move_to(lib, row, edition_folder(lib, PRINCESS, EDITION_ASIN))
    path = links_file(lib, [(row, target, "folder-edition")])
    rc, written = plan(lib, "with", "--operator-links", str(path))
    assert rc == 0 and written is not None
    assert written["operator_links"]["sha256"] == file_sha256(path)
    entry = written["relinks"][0]
    assert entry["keep_physical_identity"] is False and "operator link (folder-edition)" in entry["via"]
    assert json.dumps(written["operator_links"]).count(str(lib.top)) == 1  # the list's own path only


def test_folder_edition_refuses_a_book_that_does_not_fit_its_runtime(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    target = move_to(lib, row, edition_folder(lib, PRINCESS, EDITION_ASIN))
    with sqlite3.connect(lib.db) as connection:
        connection.execute("UPDATE Audiobooks SET Runtime = 10 WHERE Id = 1")  # 600 s against 3 s of audio
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "folder-edition")])))
    assert rc == 0 and after is not None and relinked(after) == {}
    assert "operator_links_refused_books runtime-misfit 1" in after["operator_links"]["counts"]


def test_every_check_but_the_lifted_one_still_runs(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN, SampleRate=22050)  # the row says another sample rate
    target = move_to(lib, row, edition_folder(lib, PRINCESS, EDITION_ASIN))
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "folder-edition")])))
    assert rc == 0 and after is not None and relinked(after) == {}
    assert "operator_links_refused_books row-content-disagrees 1" in after["operator_links"]["counts"]


MARIE = ("Marie", "H. Rider Haggard")
MARIE_ASIN = "B002UZKHRE"


def tag_asin(lib: Library, row: int, asin: str) -> None:
    """Write an ASIN tag into a row's file (a new object, same audio) and register that object."""
    import subprocess

    path = lib.rows[row]
    tagged = path.with_name("tagged-" + path.name)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(path), "-c", "copy", "-metadata", f"asin={asin}",
                    str(tagged)], check=True)
    os.replace(tagged, path)
    lib.set_row(row, Size=path.stat().st_size, PhysicalObjectIdentity=stored_identity(path))


def zero_row_library_book(lib: Library, book_id: int, book: tuple[str, str], asin: str) -> None:
    with sqlite3.connect(lib.db) as connection:
        connection.execute("INSERT INTO Audiobooks (Id, Title, Authors, BasePath, Asin, Runtime) VALUES "
                           "(?, ?, ?, ?, ?, 0.05)", (book_id, book[0], json.dumps([book[1]]), str(lib.root), asin))


def test_hl_other_quarantine_refuses_a_quarantine_id_naming_a_library_book(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    zero_row_library_book(lib, 9, MARIE, MARIE_ASIN)  # a library book with no rows: the HL9 pattern
    target = hardlinked(lib, row, edition_folder(lib, PRINCESS, PRINCESS_ASIN), f"[{MARIE_ASIN}] - Marie")
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "hl-other-quarantine")])))
    assert rc == 0 and after is not None and relinked(after) == {}
    assert "operator_links_refused_books names-another-library-book 1" in after["operator_links"]["counts"]


def test_an_asin_tag_naming_another_library_book_refuses_any_link(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    zero_row_library_book(lib, 9, MARIE, MARIE_ASIN)
    tag_asin(lib, row, MARIE_ASIN)  # the file says it is Marie; its quarantine id names no library book
    target = hardlinked(lib, row, edition_folder(lib, PRINCESS, PRINCESS_ASIN), f"[{Y_ASIN}] - Little Men")
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "hl-other-quarantine")])))
    assert rc == 0 and after is not None and relinked(after) == {}
    assert "operator_links_refused_books asin-tag-names-another-library-book 1" in after["operator_links"]["counts"]


def test_an_asin_tag_naming_no_library_book_does_not_refuse(lib: Library) -> None:
    (row,) = add(lib, 1, PRINCESS, PRINCESS_ASIN)
    tag_asin(lib, row, EDITION_ASIN)  # another edition's ASIN, which no library book holds
    target = move_to(lib, row, edition_folder(lib, PRINCESS, EDITION_ASIN))
    rc, after = plan(lib, "with", "--operator-links", str(links_file(lib, [(row, target, "folder-edition")])))
    assert rc == 0 and after is not None and relinked(after) == {row: str(target)}
