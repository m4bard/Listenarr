"""The "tags name another library book" veto: titles contained in this one.

Uses the throwaway libraries of ``test_size_placeholder`` (real MP3 files, stored physical identity, every row
stale after a rename). Titles, authors and series names are corpus values.
"""

from __future__ import annotations

import pytest
from test_size_placeholder import (
    LITTLE_WOMEN,
    MARIE,
    PELLUCIDAR,
    PETER_PAN,
    PLACEHOLDER,
    PRINCESS,
    QUATERMAIN,
    TANAR,
    Library,
    left_out,
    library,
    relinked,
    render,
)

from listenarr_relink import cli, matcher
from listenarr_relink.inventory import BookRow
from listenarr_relink.paths import tokens

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


def rule_before_contained_titles(tags: dict[str, str], others: dict[frozenset[str], int],
                                 own: BookRow | None, identity_match: bool) -> int | None:
    """The veto as it was before this change: a title contained in this one never counted."""
    own_tokens = frozenset(tokens(own.title)) if own else frozenset()
    tagged = matcher.tag_tokens(tags)
    own_named = identity_match and matcher.own_title_tagged(tagged, own)
    for title_tokens, book_id in others.items():
        if not title_tokens <= tagged or title_tokens <= own_tokens:
            continue
        if own_named and matcher.explained_by_own_book(title_tokens, own):
            continue
        return book_id
    return None


def explain(library: Library, capsys: pytest.CaptureFixture[str]) -> list[str]:
    """Run explain-veto on the library's plan; return its output lines."""
    capsys.readouterr()
    assert cli.main(["explain-veto", "--plan", str(library.top / "plan" / "plan.json"), "--workers", "2"]) == 0
    return capsys.readouterr().out.splitlines()


def test_explain_veto_audits_an_applied_plan_against_contained_titles(
        library: Library, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]) -> None:
    library.add_book(1, PELLUCIDAR, 1)
    (tanar,) = library.add_book(2, TANAR, 1, Size=PLACEHOLDER)
    (princess,) = library.add_book(3, PRINCESS, 1, Size=PLACEHOLDER)
    library.rewrite(tanar, PELLUCIDAR[0])
    library.move(tanar)
    library.move(princess)
    monkeypatch.setattr(matcher, "other_book_named", rule_before_contained_titles)
    _, plan = library.plan("plan")
    assert plan is not None and set(relinked(plan)) == {tanar, princess}
    monkeypatch.undo()
    lines = explain(library, capsys)
    for expected in ("relinks_in_plan 2", "relinks_unreadable_now_or_book_gone 0", "relinks_vetoed_by_this_version 1",
                     "relinks_vetoed_by_a_contained_title 1"):
        assert expected in lines
    text = "\n".join(lines).casefold()
    assert str(library.top).casefold() not in text and "pellucidar" not in text


def test_explain_veto_reproduction_control_reads_the_cited_book(library: Library,
                                                                 capsys: pytest.CaptureFixture[str]) -> None:
    """The tags name two other books; this version cites the second, the strict rule would report the first."""
    library.add_book(1, QUATERMAIN, 1, series="Allan Quatermain")
    (row,) = library.add_book(2, MARIE, 1, album="Allan Quatermain", series="Allan Quatermain", Size=PLACEHOLDER)
    library.add_book(3, LITTLE_WOMEN, 1)
    library.rewrite(row, f"{MARIE[0]} / {LITTLE_WOMEN[0]}", "Allan Quatermain")
    new = library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None
    assert left_out(plan)[row] == "physical identity matches but tags name another library book (audiobook 3)"
    assert "candidates_reason_book_not_named_now_tags_changed 0" in explain(library, capsys)
    donor = library.top / "retagged.mp3"
    render(donor, 4000, MARIE[0], "Allan Quatermain")
    new.write_bytes(donor.read_bytes())
    assert "candidates_reason_book_not_named_now_tags_changed 1" in explain(library, capsys)
