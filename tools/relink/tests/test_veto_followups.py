"""The "tags name another library book" veto: titles contained in this one.

Uses the throwaway libraries of ``test_size_placeholder`` (real MP3 files, stored physical identity, every row
stale after a rename). Titles, authors and series names are corpus values.
"""

from __future__ import annotations

import pytest
from test_size_placeholder import (
    PELLUCIDAR,
    PETER_PAN,
    PLACEHOLDER,
    TANAR,
    Library,
    left_out,
    library,
    relinked,
)

__all__ = ["library"]  # the fixture, re-exported so pytest finds it here

KENSINGTON = ("Peter Pan in Kensington Gardens", "James M. Barrie")
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


