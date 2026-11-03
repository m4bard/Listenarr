"""The "tags name another library book" veto: titles contained in this one, and series beyond the primary.

Uses the throwaway libraries of ``test_size_placeholder`` (real MP3 files, stored physical identity, every row
stale after a rename). Titles, authors and series names are corpus values.
"""

from __future__ import annotations

import sqlite3

import pytest
from test_size_placeholder import (
    PELLUCIDAR,
    PETER_PAN,
    PLACEHOLDER,
    QUATERMAIN,
    TANAR,
    Library,
    left_out,
    library,
    relinked,
)

__all__ = ["library"]  # the fixture, re-exported so pytest finds it here

KENSINGTON = ("Peter Pan in Kensington Gardens", "James M. Barrie")
SHE_AND_ALLAN = ("She And Allan", "H. Rider Haggard")
VETO_BOOK_1 = "physical identity matches but tags name another library book (audiobook 1)"

# This book, and another library book whose title is part of this one.
CONTAINED = [
    pytest.param(TANAR, PELLUCIDAR, id="pellucidar-in-tanar-of-pellucidar"),
    pytest.param(KENSINGTON, PETER_PAN, id="peter-pan-in-kensington-gardens"),
]


@pytest.mark.parametrize(("book", "other"), CONTAINED)
def test_rewrite_naming_a_title_contained_in_this_one_is_vetoed(library: Library, book: tuple[str, str],
                                                                 other: tuple[str, str]) -> None:
    """This object rewritten in place with the other book's recording, tagged with the other title only."""
    library.add_book(1, other, 1)
    (row,) = library.add_book(2, book, 1, Size=PLACEHOLDER)
    library.rewrite(row, other[0])
    library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert left_out(plan)[row] == VETO_BOOK_1


@pytest.mark.parametrize(("book", "other"), CONTAINED)
def test_own_tags_carrying_this_whole_title_relink_beside_a_contained_title(
        library: Library, book: tuple[str, str], other: tuple[str, str]) -> None:
    library.add_book(1, other, 1)
    (row,) = library.add_book(2, book, 1, album=f"{book[1]} - {book[0]}", Size=PLACEHOLDER)
    new = library.move(row)
    rc, plan = library.plan("plan")
    assert rc == 0 and plan is not None
    assert relinked(plan) == {row: str(new)}


def test_own_tags_naming_only_a_contained_title_are_vetoed(library: Library) -> None:
    """Accepted cost: this book's own file whose tags name only the shorter title looks like that book's file."""
    library.add_book(1, PELLUCIDAR, 1)
    (row,) = library.add_book(2, TANAR, 1, album=PELLUCIDAR[0], Size=PLACEHOLDER)
    library.rewrite(row, "Chapter 1", PELLUCIDAR[0])
    library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert left_out(plan)[row] == VETO_BOOK_1


def test_shape_candidate_tagged_with_a_contained_title_is_vetoed(library: Library) -> None:
    library.add_book(1, PELLUCIDAR, 1)
    (row,) = library.add_book(2, TANAR, 1, album=PELLUCIDAR[0])
    library.rewrite(row, PELLUCIDAR[0])
    library.set_row(row, PhysicalObjectIdentity=None, Size=library.rows[row].stat().st_size)
    library.move(row)
    _, plan = library.plan("plan", "--include-shape-matches")
    assert plan is not None and relinked(plan) == {}
    assert left_out(plan)[row] == "no same-size file agrees with the stored audio facts"
    (candidate,) = plan["left_out"][0]["candidates"]
    assert candidate["rejected"] == "tags name another library book (audiobook 1)"


def add_secondary_series(library: Library, book_id: int, primary: str, secondary: str) -> None:
    """Record the book's series as .620 does: the primary one in Audiobooks.Series and both as memberships."""
    with sqlite3.connect(library.db) as connection:
        connection.executemany("INSERT INTO AudiobookSeriesMemberships (AudiobookId, SeriesName, IsPrimary, "
                               "SortOrder) VALUES (?, ?, ?, ?)",
                               [(book_id, primary, 1, 0), (book_id, secondary, 0, 1)])


def test_identity_match_tagged_with_a_secondary_series_relinks(library: Library) -> None:
    library.add_book(1, QUATERMAIN, 1, series="Allan Quatermain")
    (row,) = library.add_book(2, SHE_AND_ALLAN, 1, album="Allan Quatermain", series="Ayesha", Size=PLACEHOLDER)
    add_secondary_series(library, 2, "Ayesha", "Allan Quatermain")
    new = library.move(row)
    rc, plan = library.plan("plan")
    assert rc == 0 and plan is not None
    assert relinked(plan) == {row: str(new)}
    assert library.apply("plan") == 0
    assert library.paths()[row] == str(new)


def test_secondary_series_book_content_swap_stays_vetoed(library: Library) -> None:
    """The secondary series explains the other title, but the swapped-in tags do not carry this book's title."""
    (other_row,) = library.add_book(1, QUATERMAIN, 1, series="Allan Quatermain", Size=PLACEHOLDER)
    (row,) = library.add_book(2, SHE_AND_ALLAN, 1, album="Allan Quatermain", series="Ayesha", Size=PLACEHOLDER)
    add_secondary_series(library, 2, "Ayesha", "Allan Quatermain")
    library.rewrite(row, QUATERMAIN[0])
    library.rewrite(other_row, SHE_AND_ALLAN[0], "Allan Quatermain")
    library.move(row)
    library.move(other_row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    reasons = left_out(plan)
    assert reasons[row] == VETO_BOOK_1
    assert reasons[other_row] == "physical identity matches but tags name another library book (audiobook 2)"


def test_database_without_series_memberships_reads_the_primary_series_only(library: Library) -> None:
    library.add_book(1, QUATERMAIN, 1, series="Allan Quatermain")
    (row,) = library.add_book(2, SHE_AND_ALLAN, 1, album="Allan Quatermain", series="Ayesha", Size=PLACEHOLDER)
    with sqlite3.connect(library.db) as connection:
        connection.execute("DROP TABLE AudiobookSeriesMemberships")
    library.move(row)
    rc, plan = library.plan("plan")
    assert rc == 0 and plan is not None and relinked(plan) == {}
    assert left_out(plan)[row] == VETO_BOOK_1

