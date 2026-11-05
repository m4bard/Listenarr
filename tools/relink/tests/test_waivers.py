"""The two narrowed rules: a hard link whose other names are quarantine names, and tags against the folder id.

(a) ``nlink > 1`` is matched only when every other name lies under a ``--quarantine-dir`` folder, outside
every library root, on the file's own device, in a folder whose bracketed id is the book's id.
(b) "tags name another library book" is waived only when the stored file handle and inode are the file's own
and the folder's bracketed id is the book's ``Asin``.

Fake identities as in test_identity_drift.py (identity.read_live and media.probe replaced); the CLI tests at
the end use real files. Titles, authors and ASINs are corpus values.
"""

from __future__ import annotations

import dataclasses
import os
import sqlite3
from pathlib import Path

import pytest
from test_identity_drift import (
    DURATION,
    JUNGLE,
    LIVE_DEV,
    LIVE_MINOR,
    OLD_MINOR,
    OTHER_MINOR,
    PRINCESS,
    ROOT,
    Fake,
    outcome,
    renamed,
    spelled,
)
from test_size_placeholder import PLACEHOLDER, Library, left_out, library, relinked

from listenarr_relink import cli, physical
from listenarr_relink.explain_identity import explain_identity, row_lines
from listenarr_relink.identity import LiveIdentity
from listenarr_relink.inventory import QuarantineName

__all__ = ["library"]  # the fixture, re-exported so pytest finds it here

QUARANTINE = "/QUARANTINE-DUP"
PRINCESS_ASIN = PRINCESS[2]
TAGS_VETOED = "vetoed:identity-inconsistent-tags-name-another-book"


def quarantine(fake: Fake, ino: int, *, folder: str = f"{PRINCESS[0]} [{PRINCESS_ASIN}]", dev: int = LIVE_DEV,
               where: str = QUARANTINE) -> None:
    """Record a quarantine name (another link of inode ``ino``) the way walk_quarantine would."""
    host_path = f"{where}/{PRINCESS[1]}/{folder}/01.mp3"
    fake.library.quarantine.setdefault(ino, []).append(QuarantineName(host_path, where, dev, ino))


def hardlinked_princess(nlink: int = 2) -> Fake:
    """A stale row of A Princess of Mars whose object, now renamed under the root, has ``nlink`` names."""
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS), 1234, 77, nlink=nlink)
    return fake


# (a) hard links into a quarantine folder


def test_a_quarantine_name_outside_the_roots_matches(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = hardlinked_princess()
    quarantine(fake, 1234)
    result = fake.run(monkeypatch)
    (decision,) = result.decisions
    assert outcome(result)[1][0] == "matched"
    assert decision.chosen is not None and decision.chosen.file.db_path == renamed(PRINCESS)  # the in-root name
    assert decision.chosen.hardlinked_to_quarantine and not decision.chosen.tags_waived
    lines = explain_identity(fake.library, result)
    for expected in ("  matched 1", f"  {physical.HARDLINKED} 0", "matched_hardlinked_to_quarantine 1",
                     "hardlinked_to_quarantine_before_vetoes 1", "matched_tags_disagree_folder_agrees 0",
                     "quarantine_names_with_links 1"):
        assert expected in lines, expected
    assert row_lines(result, with_paths=False)[1].split("\t")[-1] == "hardlinked-to-quarantine"
    assert not any("QUARANTINE" in line for line in lines)


def test_without_quarantine_dirs_a_hard_link_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = hardlinked_princess()
    result = fake.run(monkeypatch)
    assert result.decisions[0].chosen is None and outcome(result)[1][0] == physical.HARDLINKED
    assert "matched_hardlinked_to_quarantine 0" in explain_identity(fake.library, result)


def test_a_quarantine_name_inside_a_root_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = hardlinked_princess()
    quarantine(fake, 1234, where=f"{ROOT}/quarantine")
    assert outcome(fake.run(monkeypatch))[1][0] == physical.HARDLINKED


def test_another_name_outside_the_quarantine_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = hardlinked_princess(nlink=3)  # one quarantine name; the third name is somewhere else
    quarantine(fake, 1234)
    assert outcome(fake.run(monkeypatch))[1][0] == physical.HARDLINKED


def test_a_quarantine_name_with_another_bracketed_id_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = hardlinked_princess()
    quarantine(fake, 1234, folder=f"{JUNGLE[0]} [{JUNGLE[2]}]")
    assert outcome(fake.run(monkeypatch))[1][0] == physical.HARDLINKED


def test_a_quarantine_name_without_a_bracketed_id_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = hardlinked_princess()
    quarantine(fake, 1234, folder=PRINCESS[0])
    assert outcome(fake.run(monkeypatch))[1][0] == physical.HARDLINKED


def test_a_book_with_no_id_refuses_even_when_no_folder_has_one(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS, asin=False)
    fake.library.books[1].values["BasePath"] = f"{ROOT}/{PRINCESS[1]}/{PRINCESS[0]}"  # no bracketed id either
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(f"{ROOT}/{PRINCESS[1]}/{PRINCESS[0]} (2012)/01.mp3", 1234, 77, nlink=2)
    quarantine(fake, 1234, folder=PRINCESS[0])  # neither the book nor either folder has an id: None == None
    assert outcome(fake.run(monkeypatch))[1][0] == physical.HARDLINKED


def test_a_quarantine_name_on_another_dataset_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = hardlinked_princess()
    # The same inode number on another dataset is another object, not the second name of this one.
    quarantine(fake, 1234, dev=os.makedev(0, OTHER_MINOR))
    assert outcome(fake.run(monkeypatch))[1][0] == physical.HARDLINKED


def test_the_in_root_folder_must_still_agree(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS, bracket=JUNGLE[2]), 1234, 77, nlink=2)
    quarantine(fake, 1234)
    assert outcome(fake.run(monkeypatch))[1][0] == physical.FOLDER_ID_DISAGREES


# (b) tags naming another book, against the folder's bracketed id


def tagged_as_jungle(stored: str, *, folder_bracket: bool = True, asin: bool = True) -> Fake:
    """A stale row of A Princess of Mars whose file's tags fully name The Jungle Book."""
    fake = Fake()
    fake.book(1, PRINCESS, asin=asin)
    fake.book(2, JUNGLE)
    fake.row(1, 1, stored)
    path = renamed(PRINCESS) if folder_bracket else f"{ROOT}/{PRINCESS[1]}/{PRINCESS[0]}/01.mp3"
    fake.file(path, 1234, 77, title=JUNGLE[0])
    return fake


def test_folder_agrees_and_handle_equal_matches(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = tagged_as_jungle(spelled(OLD_MINOR, 1234, 77))
    result = fake.run(monkeypatch)
    (decision,) = result.decisions
    assert outcome(result)[1][0] == "matched"
    assert decision.chosen is not None and decision.chosen.tags_waived
    lines = explain_identity(fake.library, result)
    assert "matched_tags_disagree_folder_agrees 1" in lines and "tags_disagree_folder_agrees_waived 1" in lines
    assert row_lines(result, with_paths=False)[1].split("\t")[-1] == "tags-disagree-folder-agrees"


def test_folder_agrees_and_handle_generation_differs_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = tagged_as_jungle(spelled(OLD_MINOR, 1234, 78))  # the inode was reused: no identity match
    fake.library.files[0].values["Size"] = 4_000_000  # a real stored size: the file is a size candidate
    result = fake.run(monkeypatch, identity_only=False)
    (decision,) = result.decisions
    assert decision.identity_class == physical.HANDLE_MISMATCH and decision.chosen is None
    assert str(decision.candidates[0].rejected).startswith("tags name another library book")


def test_a_generation_only_identity_keeps_the_tag_veto(monkeypatch: pytest.MonkeyPatch) -> None:
    # Same device, inode and inode generation: an identity match, but no file handle to compare.
    fake = tagged_as_jungle(f"linux-generation:00000000:{LIVE_MINOR:08x}:{1234:016x}:gen:0000004d")
    fake.live[fake.library.disk[0].host_path] = LiveIdentity(0, LIVE_MINOR, 1234, frozenset({"gen:0000004d"}))
    assert outcome(fake.run(monkeypatch))[1][0] == TAGS_VETOED


def test_folder_disagrees_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.book(2, JUNGLE)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS, bracket=JUNGLE[2]), 1234, 77, title=JUNGLE[0])
    result = fake.run(monkeypatch)
    assert result.decisions[0].chosen is None and outcome(result)[1][0] == physical.FOLDER_ID_DISAGREES


def test_folder_id_missing_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = tagged_as_jungle(spelled(OLD_MINOR, 1234, 77), folder_bracket=False)
    assert outcome(fake.run(monkeypatch))[1][0] == TAGS_VETOED


def test_a_book_without_an_asin_keeps_the_tag_veto(monkeypatch: pytest.MonkeyPatch) -> None:
    # The stored folder's id agrees (so the folder check passes), but the waiver needs the book's own Asin.
    fake = tagged_as_jungle(spelled(OLD_MINOR, 1234, 77), asin=False)
    result = fake.run(monkeypatch)
    assert result.decisions[0].folder_id == "agrees" and outcome(result)[1][0] == TAGS_VETOED


def test_the_waiver_keeps_the_duration_veto(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = tagged_as_jungle(spelled(OLD_MINOR, 1234, 77))
    fake.library.files[0].values["DurationSeconds"] = DURATION / 2
    assert outcome(fake.run(monkeypatch))[1][0] == "vetoed:identity-inconsistent-duration"


def test_both_waivers_on_one_row_are_both_reported(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = tagged_as_jungle(spelled(OLD_MINOR, 1234, 77))
    fake.library.disk[0] = dataclasses.replace(fake.library.disk[0], nlink=2)
    quarantine(fake, 1234)
    result = fake.run(monkeypatch)
    assert outcome(result)[1][0] == "matched"
    assert row_lines(result, with_paths=False)[1].split("\t")[-1] == (
        "hardlinked-to-quarantine,tags-disagree-folder-agrees")


# (a) end to end, on real files


def bracketed_and_quarantined(library: Library) -> tuple[int, Path, Path]:
    """One placeholder-size row moved into '<title> [<Asin>]', with a second name under QUARANTINE-DUP."""
    (row,) = library.add_book(1, PRINCESS[:2], 1, Size=PLACEHOLDER)
    with sqlite3.connect(library.db) as connection:
        connection.execute("ALTER TABLE Audiobooks ADD COLUMN Asin TEXT")
        connection.execute("UPDATE Audiobooks SET Asin = ? WHERE Id = 1", (PRINCESS_ASIN,))
    old = library.rows[row]
    new = old.parent.with_name(f"{old.parent.name} [{PRINCESS_ASIN}]") / old.name
    new.parent.mkdir()
    os.rename(old, new)
    held = library.top / "QUARANTINE-DUP" / PRINCESS[1] / new.parent.name / old.name
    held.parent.mkdir(parents=True)
    os.link(new, held)
    return row, new, held


def test_cli_relinks_the_in_root_name_and_applies(library: Library, capsys: pytest.CaptureFixture[str]) -> None:
    row, new, held = bracketed_and_quarantined(library)
    _, refused = library.plan("plan-without")
    assert refused is not None and relinked(refused) == {} and "hard links" in left_out(refused)[row]
    rc, plan = library.plan("plan", "--quarantine-dir", str(held.parents[2]))
    assert rc == 0 and plan is not None and relinked(plan) == {row: str(new)}
    assert plan["relinks"][0]["evidence"]["identity_hardlinked_to_quarantine"] is True
    assert plan["summary"]["relinks_hardlinked_to_quarantine"] == 1
    capsys.readouterr()
    assert cli.main(["explain-identity", "--db", str(library.db), "--quarantine-dir", str(held.parents[2])]) == 0
    out = capsys.readouterr().out.splitlines()
    assert "matched_hardlinked_to_quarantine 1" in out and "quarantine_names_with_links 1" in out
    assert not any("QUARANTINE" in line or str(library.top) in line for line in out)
    assert library.apply("plan") == 0
    assert library.paths() == {row: str(new)} and held.exists()


def test_cli_refuses_a_quarantine_dir_inside_or_holding_a_root_missing_or_given_twice(library: Library) -> None:
    _, _, held = bracketed_and_quarantined(library)
    quarantine_dir = str(held.parents[2])
    inside = library.root / "quarantine"
    inside.mkdir()
    for extra in ([str(inside)], [str(library.top)], [str(library.top / "missing")],
                  [quarantine_dir, quarantine_dir], [quarantine_dir, str(held.parent)]):
        rc, plan = library.plan(f"plan-{len(list(library.top.iterdir()))}", "--quarantine-dir", *extra)
        assert rc == 2 and plan is None, extra
        assert cli.main(["explain-identity", "--db", str(library.db), "--quarantine-dir", *extra]) == 2, extra
