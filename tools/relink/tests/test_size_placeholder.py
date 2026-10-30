"""PLAN/APPLY on small real libraries: the Size=64 placeholder, the guards it must not weaken, and exclusions.

Also the "tags name another library book" veto: what an identity match lets through, and what it still stops.

Each test builds a library of real MP3 files (ffmpeg), records each file's physical identity in .620's
persisted spelling, then moves the files (a rename keeps the object) so every row is stale. Titles and
authors are corpus values.
"""

from __future__ import annotations

import dataclasses
import json
import os
import shutil
import sqlite3
import subprocess
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import pytest

from listenarr_relink import cli, identity, matcher, media

PRINCESS = ("A Princess of Mars", "Edgar Rice Burroughs")
JUNGLE = ("The Jungle Book", "Rudyard Kipling")
QUATERMAIN = ("Allan Quatermain", "H. Rider Haggard")
MARIE = ("Marie", "H. Rider Haggard")
LITTLE_WOMEN = ("Little Women", "Louisa May Alcott")
LITTLE_MEN = ("Little Men", "Louisa May Alcott")
PELLUCIDAR = ("Pellucidar", "Edgar Rice Burroughs")
TANAR = ("Tanar of Pellucidar", "Edgar Rice Burroughs")
FAUST = ("Faust", "Johann Wolfgang von Goethe")
FAUST_SUBTITLE = "Der Tragödie erster Teil"
RECLAM_FAUST = ("Goethe.Faust. Der Tragödie Erster Teil", "Johann Wolfgang von Goethe")
PLACEHOLDER = 64

SCHEMA = """
CREATE TABLE RootFolders (Id INTEGER PRIMARY KEY, Path TEXT);
CREATE TABLE Audiobooks (Id INTEGER PRIMARY KEY, Title TEXT, Authors TEXT, BasePath TEXT, FilePath TEXT,
    Series TEXT, Subtitle TEXT);
CREATE TABLE AudiobookFiles (Id INTEGER PRIMARY KEY, AudiobookId INTEGER, Path TEXT, Size INTEGER,
    DurationSeconds REAL, Codec TEXT, SampleRate INTEGER, Channels INTEGER, PhysicalObjectIdentity TEXT,
    PhysicalIdentityObservedAtUtc TEXT, CanonicalPath TEXT, PathIdentityState TEXT, PathIdentityLookupKey TEXT,
    PathOwnershipKey TEXT, PathIdentityReason TEXT);
"""

pytestmark = pytest.mark.skipif(shutil.which("ffmpeg") is None or shutil.which("ffprobe") is None,
                                reason="needs ffmpeg and ffprobe")


def stored_identity(path: Path) -> str:
    """The identity .620 persists for ``path``: device, inode and the file handle (else the inode generation)."""
    live = identity.read_live(str(path))
    assert live is not None
    strong = sorted(live.generations, key=lambda g: not g.startswith("fh:"))
    if not strong:
        pytest.skip("this filesystem gives no strong physical identity")
    return f"linux-generation:{live.major:08x}:{live.minor:08x}:{live.inode:016x}:{strong[0]}"


def render(path: Path, frequency: int, title: str, album: str | None = None) -> dict[str, Any]:
    """Write a 3 s MP3 tagged ``title`` and ``album`` (default: the title); return the facts .620 stores for it."""
    path.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", f"sine=frequency={frequency}:duration=3",
                    "-c:a", "libmp3lame", "-b:a", "64k", "-ar", "44100", "-ac", "1", "-metadata", f"title={title}",
                    "-metadata", f"album={album or title}", str(path)], check=True)
    probe = json.loads(subprocess.run(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json",
                                       str(path)], check=True, capture_output=True, text=True).stdout)
    stream = probe["streams"][0]
    return {"Size": path.stat().st_size, "DurationSeconds": float(probe["format"]["duration"]),
            "Codec": stream["codec_name"], "SampleRate": int(stream["sample_rate"]), "Channels": stream["channels"]}


@dataclass
class Library:
    """A throwaway library: ROOT-A on disk plus a database holding its rows."""

    top: Path
    rows: dict[int, Path] = field(default_factory=dict)
    next_row: int = 1

    @property
    def root(self) -> Path:
        """ROOT-A."""
        return self.top / "ROOT-A"

    @property
    def db(self) -> Path:
        """The database file."""
        return self.top / "db.sqlite"

    def __post_init__(self) -> None:
        self.root.mkdir(parents=True)
        with sqlite3.connect(self.db) as connection:
            connection.executescript(SCHEMA)
            connection.execute("INSERT INTO RootFolders (Id, Path) VALUES (1, ?)", (str(self.root),))

    def add_book(self, book_id: int, book: tuple[str, str], parts: int, *, album: str | None = None,
                 series: str | None = None, subtitle: str | None = None, **overrides: Any) -> list[int]:
        """Create a book's files at their stored paths and insert its rows; ``overrides`` replace stored facts."""
        title, author = book
        folder = self.root / author / title
        ids: list[int] = []
        with sqlite3.connect(self.db) as connection:
            for part in range(1, parts + 1):
                path = folder / f"{title} - 0{part}.mp3"
                facts = {**render(path, 200 + 100 * book_id + 10 * part, title, album), **overrides}
                row = {"Id": self.next_row, "AudiobookId": book_id, "Path": str(path),
                       "PhysicalObjectIdentity": stored_identity(path), "PathIdentityState": "Valid", **facts}
                connection.execute(f"INSERT INTO AudiobookFiles ({', '.join(row)}) VALUES "
                                   f"({', '.join('?' * len(row))})", list(row.values()))
                self.rows[self.next_row] = path
                ids.append(self.next_row)
                self.next_row += 1
            connection.execute("INSERT INTO Audiobooks (Id, Title, Authors, BasePath, FilePath, Series, Subtitle) "
                               "VALUES (?, ?, ?, ?, ?, ?, ?)", (book_id, title, json.dumps([author]), str(folder),
                                                                str(self.rows[ids[0]]), series, subtitle))
        return ids

    def set_row(self, row_id: int, **values: Any) -> None:
        """Overwrite stored columns of one row."""
        with sqlite3.connect(self.db) as connection:
            connection.execute(f"UPDATE AudiobookFiles SET {', '.join(f'{k} = ?' for k in values)} WHERE Id = ?",
                               [*values.values(), row_id])

    def move(self, row_id: int) -> Path:
        """Rename a row's file into '<title> (2012)/' next to its folder (the object, and its identity, stay)."""
        old = self.rows[row_id]
        new = old.parent.with_name(f"{old.parent.name} (2012)") / old.name
        new.parent.mkdir(exist_ok=True)
        os.rename(old, new)
        return new

    def rewrite(self, row_id: int, title: str, album: str | None = None) -> None:
        """Write another recording, tagged ``title``/``album``, into a row's file in place (same object)."""
        donor = self.top / "donor" / f"{row_id}.mp3"
        render(donor, 1000 + row_id, title, album)
        self.rows[row_id].write_bytes(donor.read_bytes())

    def plan(self, name: str, *extra: str) -> tuple[int, dict[str, Any] | None]:
        """Run PLAN (default identity-only mode); return the exit code and the plan, if written."""
        out = self.top / name
        rc = cli.main(["plan", "--db", str(self.db), "--out", str(out), "--workers", "2", *extra])
        plan_file = out / "plan.json"
        return rc, json.loads(plan_file.read_text()) if plan_file.exists() else None

    def apply(self, plan_name: str) -> int:
        """APPLY a written plan to the library database (service-stopped preconditions satisfied)."""
        plan_file = self.top / plan_name / "plan.json"
        backup = self.top / f"{plan_name}-backup.sqlite"
        shutil.copy(self.db, backup)
        digest = (self.top / plan_name / "plan-summary.txt").read_text().split()[3]
        return cli.main(["apply", "--db", str(self.db), "--plan", str(plan_file), "--plan-sha256", digest,
                         "--backup", str(backup), "--confirm-service-stopped"])

    def paths(self) -> dict[int, str]:
        """Stored path of every row."""
        with sqlite3.connect(self.db) as connection:
            return {int(i): str(p) for i, p in connection.execute("SELECT Id, Path FROM AudiobookFiles")}


def left_out(plan: dict[str, Any]) -> dict[int, str]:
    """Left-out row id -> reason."""
    return {int(item["row_id"]): str(item["reason"]) for item in plan["left_out"]}


def relinked(plan: dict[str, Any]) -> dict[int, str]:
    """Re-linked row id -> new path."""
    return {int(item["row_id"]): str(item["new_path"]) for item in plan["relinks"]}


@pytest.fixture
def library(tmp_path: Path) -> Library:
    """An empty library."""
    return Library(tmp_path)


def test_placeholder_size_with_identity_match_relinks(library: Library) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER)
    new = library.move(row)
    rc, plan = library.plan("plan")
    assert rc == 0 and plan is not None
    assert relinked(plan) == {row: str(new)}
    entry = plan["relinks"][0]
    assert entry["confidence"] == "identity" and entry["keep_physical_identity"] is True
    assert entry["evidence"]["stored_size_placeholder"] is True
    assert plan["summary"]["relinks_over_placeholder_size"] == 1
    assert library.apply("plan") == 0
    assert library.paths() == {row: str(new)}


def test_real_size_difference_with_identity_match_stays_left_out(library: Library) -> None:
    (row,) = library.add_book(1, PRINCESS, 1)
    library.set_row(row, Size=library.rows[row].stat().st_size + 4096)
    library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert "size changed" in left_out(plan)[row]


def test_64_without_a_stored_duration_is_a_real_size(library: Library) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER, DurationSeconds=None)
    library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert "size changed (64 ->" in left_out(plan)[row]


def test_64_with_a_sub_second_duration_is_a_real_size(library: Library) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER, DurationSeconds=0.5)
    library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert "size changed (64 ->" in left_out(plan)[row]


def test_real_64_byte_file_is_never_linked_as_audio(library: Library) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER)
    path = library.rows[row]
    with path.open("r+b") as handle:  # same object, now really 64 bytes and no longer audio
        handle.truncate(PLACEHOLDER)
    library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert "physical identity matches but not readable as audio" in left_out(plan)[row]


def test_placeholder_size_does_not_waive_the_stored_duration(library: Library) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER)
    with sqlite3.connect(library.db) as connection:
        connection.execute("UPDATE AudiobookFiles SET DurationSeconds = DurationSeconds + 30 WHERE Id = ?", (row,))
    library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert "physical identity matches but duration" in left_out(plan)[row]


def test_placeholder_size_needs_a_readable_file_duration(library: Library, monkeypatch: pytest.MonkeyPatch) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER)
    library.move(row)
    real_probe = media.probe
    monkeypatch.setattr(media, "probe", lambda path: dataclasses.replace(real_probe(path), duration=None))
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert "duration, which stands in for it, could not be read" in left_out(plan)[row]


def test_infinite_duration_is_no_placeholder(library: Library) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER, DurationSeconds=float("inf"))
    library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert "size changed (64 ->" in left_out(plan)[row]


def test_non_finite_stored_duration_never_agrees(library: Library) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, DurationSeconds=float("inf"))
    library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert "is not a finite number" in left_out(plan)[row]


def test_inode_reuse_with_another_generation_is_refused(library: Library) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER)
    with sqlite3.connect(library.db) as connection:
        (stored,) = connection.execute("SELECT PhysicalObjectIdentity FROM AudiobookFiles WHERE Id = ?",
                                       (row,)).fetchone()
    head, handle = stored.rsplit(":", 1)
    other = f"{int(handle[:8], 16) ^ 1:08x}{handle[8:]}"  # same device and inode, another object generation
    library.set_row(row, PhysicalObjectIdentity=f"{head}:{other}")
    library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert "no physical identity match" in left_out(plan)[row]


def test_placeholder_book_with_a_vanished_part_is_withheld_whole(library: Library) -> None:
    rows = library.add_book(1, PRINCESS, 3, Size=PLACEHOLDER)
    library.move(rows[0])
    library.move(rows[1])
    library.rows[rows[2]].unlink()
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    reasons = left_out(plan)
    assert all("partly linked" in reasons[r] for r in rows[:2])


def test_excluded_rows_are_withheld_with_their_whole_book(library: Library) -> None:
    princess = library.add_book(1, PRINCESS, 2, Size=PLACEHOLDER)
    (jungle,) = library.add_book(2, JUNGLE, 1, Size=PLACEHOLDER)
    before = library.paths()
    for row in (*princess, jungle):
        library.move(row)
    exclude = library.top / "exclude.txt"
    exclude.write_text(f"# rows a journal also names\n{princess[0]}\n\n")
    rc, plan = library.plan("plan", "--exclude-file-ids", str(exclude))
    assert rc == 0 and plan is not None
    assert set(relinked(plan)) == {jungle}
    reasons = left_out(plan)
    assert "--exclude-file-ids" in reasons[princess[0]]
    assert "partly linked" in reasons[princess[1]]
    assert plan["excluded_file_ids"] == [princess[0]]
    assert plan["summary"]["left_out_by_reason"]["excluded"] == 1
    assert library.apply("plan") == 0
    after = library.paths()
    assert {r: after[r] for r in princess} == {r: before[r] for r in princess}
    assert after[jungle] != before[jungle]


def test_excluded_row_withholds_its_book_despite_a_live_sibling(library: Library) -> None:
    rows = library.add_book(1, PRINCESS, 3, Size=PLACEHOLDER)
    before = library.paths()
    library.move(rows[0])
    library.move(rows[1])
    exclude = library.top / "exclude.txt"
    exclude.write_text(f"{rows[0]}\n")
    rc, plan = library.plan("plan", "--exclude-file-ids", str(exclude))
    assert rc == 0 and plan is not None
    assert relinked(plan) == {}
    assert "its book holds row" in left_out(plan)[rows[1]]
    assert plan["summary"]["left_out_by_reason"] == {"excluded": 1, "withheld-excluded-book": 1}
    assert library.apply("plan") == 0
    assert library.paths() == before


@pytest.mark.parametrize("content", ["", "# only a comment\n\n"])
def test_exclude_list_naming_no_id_is_refused(library: Library, content: str) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER)
    library.move(row)
    exclude = library.top / "exclude.txt"
    exclude.write_text(content)
    rc, plan = library.plan("plan", "--exclude-file-ids", str(exclude))
    assert rc == 2 and plan is None


def test_exclude_list_naming_no_file_row_is_refused(library: Library) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER)
    library.move(row)
    exclude = library.top / "exclude.txt"
    exclude.write_text(f"{row}\n{row + 1000}\n")
    rc, plan = library.plan("plan", "--exclude-file-ids", str(exclude))
    assert rc == 2 and plan is None
    assert not (library.top / "plan").exists()


def test_malformed_exclude_list_is_refused(library: Library) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER)
    library.move(row)
    exclude = library.top / "exclude.txt"
    exclude.write_text(f"{row}, 7\n")
    rc, plan = library.plan("plan", "--exclude-file-ids", str(exclude))
    assert rc == 2 and plan is None


# Each case: this book, its series, the other library book its own tags name, and its album tag. The other
# title is not part of this one, so the strict veto rejects the file; its words are this book's series.
SERIES_IN_TAGS = [
    pytest.param(MARIE, "Allan Quatermain", QUATERMAIN, "Allan Quatermain", id="series-is-another-title"),
    pytest.param(LITTLE_MEN, "Little Women", LITTLE_WOMEN, "Little Women", id="series-shares-a-word"),
]


@pytest.mark.parametrize(("book", "series", "other", "album"), SERIES_IN_TAGS)
def test_identity_match_tagged_with_own_series_relinks(library: Library, book: tuple[str, str], series: str,
                                                       other: tuple[str, str], album: str) -> None:
    library.add_book(1, other, 1, series=series)
    (row,) = library.add_book(2, book, 1, album=album, series=series, Size=PLACEHOLDER)
    new = library.move(row)
    rc, plan = library.plan("plan")
    assert rc == 0 and plan is not None
    assert relinked(plan) == {row: str(new)}
    assert library.apply("plan") == 0
    assert library.paths()[row] == str(new)


@pytest.mark.parametrize(("book", "series", "other", "album"), SERIES_IN_TAGS)
def test_cross_book_content_swap_stays_vetoed(library: Library, book: tuple[str, str], series: str,
                                              other: tuple[str, str], album: str) -> None:
    """Each object now holds the other book's recording and tags: identity matches, the content does not."""
    (other_row,) = library.add_book(1, other, 1, series=series, Size=PLACEHOLDER)
    (row,) = library.add_book(2, book, 1, album=album, series=series, Size=PLACEHOLDER)
    library.rewrite(row, other[0])
    library.rewrite(other_row, book[0], album)
    library.move(row)
    library.move(other_row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    reasons = left_out(plan)
    assert reasons[row] == "physical identity matches but tags name another library book (audiobook 1)"
    assert reasons[other_row] == "physical identity matches but tags name another library book (audiobook 2)"


# Content rewritten into this book's object whose tags carry this book's whole title as well as the other's.
REWRITES = [
    # The other title's extra word (Tanar) is not this book's text: carrying this title is not enough.
    pytest.param(PELLUCIDAR, None, None, TANAR, None, id="other-title-contains-this-one"),
    # The other edition's extra words are this book's subtitle: the subtitle explains nothing.
    pytest.param(FAUST, FAUST_SUBTITLE, None, RECLAM_FAUST, f"{FAUST[1]} - {FAUST[0]}: {FAUST_SUBTITLE}",
                 id="other-edition-named-by-subtitle"),
]


@pytest.mark.parametrize(("book", "subtitle", "series", "other", "album"), REWRITES)
def test_rewrite_naming_a_title_that_contains_this_one_stays_vetoed(
        library: Library, book: tuple[str, str], subtitle: str | None, series: str | None, other: tuple[str, str],
        album: str | None) -> None:
    library.add_book(1, other, 1, series=series)
    (row,) = library.add_book(2, book, 1, subtitle=subtitle, series=series, Size=PLACEHOLDER)
    library.rewrite(row, other[0], album)
    library.move(row)
    _, plan = library.plan("plan")
    assert plan is not None and relinked(plan) == {}
    assert left_out(plan)[row] == "physical identity matches but tags name another library book (audiobook 1)"


def test_shape_candidate_tagged_with_own_series_stays_vetoed(library: Library) -> None:
    library.add_book(1, QUATERMAIN, 1, series="Allan Quatermain")
    (row,) = library.add_book(2, MARIE, 1, album="Allan Quatermain", series="Allan Quatermain")
    library.set_row(row, PhysicalObjectIdentity=None)
    library.move(row)
    _, plan = library.plan("plan", "--include-shape-matches")
    assert plan is not None and relinked(plan) == {}
    assert left_out(plan)[row] == "no same-size file agrees with the stored audio facts"
    (candidate,) = plan["left_out"][0]["candidates"]
    assert candidate["rejected"] == "tags name another library book (audiobook 1)"


def test_database_without_a_series_column_keeps_the_strict_veto(library: Library) -> None:
    library.add_book(1, QUATERMAIN, 1, series="Allan Quatermain")
    (row,) = library.add_book(2, MARIE, 1, album="Allan Quatermain", series="Allan Quatermain", Size=PLACEHOLDER)
    with sqlite3.connect(library.db) as connection:
        connection.execute("ALTER TABLE Audiobooks DROP COLUMN Series")
    library.move(row)
    rc, plan = library.plan("plan")
    assert rc == 0 and plan is not None and relinked(plan) == {}
    assert left_out(plan)[row] == "physical identity matches but tags name another library book (audiobook 1)"


def test_explain_veto_prints_counts_only(library: Library, monkeypatch: pytest.MonkeyPatch,
                                         capsys: pytest.CaptureFixture[str]) -> None:
    library.add_book(1, QUATERMAIN, 1, series="Allan Quatermain")
    (marie,) = library.add_book(2, MARIE, 1, album="Allan Quatermain", series="Allan Quatermain", Size=PLACEHOLDER)
    library.add_book(3, TANAR, 1)
    (pellucidar,) = library.add_book(4, PELLUCIDAR, 1, Size=PLACEHOLDER)
    library.rewrite(pellucidar, TANAR[0])
    library.move(marie)
    library.move(pellucidar)
    strict = matcher.other_book_named  # a plan written before identity could waive the veto
    monkeypatch.setattr(matcher, "other_book_named", lambda tags, others, own, _: strict(tags, others, own, False))
    _, plan = library.plan("plan")
    assert plan is not None and set(left_out(plan)) == {marie, pellucidar}
    monkeypatch.undo()
    capsys.readouterr()
    assert cli.main(["explain-veto", "--plan", str(library.top / "plan" / "plan.json"), "--workers", "2"]) == 0
    lines = capsys.readouterr().out.splitlines()
    for expected in ("rows_left_out_with_veto_reason 2", "pairs_from_reasons 2", "pairs_named_now_strict 2",
                     "candidates_strict_rule_not_reproduced_tags_changed 0", "candidates_cleared_by_this_version 1",
                     "rows_whose_every_vetoed_candidate_clears 1 (a cleared veto is not a relink: re-run PLAN "
                     "for that)",
                     "       1 identity=Y one_token=N own_title_tagged=Y own_title_tag_exact=Y "
                     "explained_authors_series=Y explained_with_subtitle=Y",
                     "       1 identity=Y one_token=N own_title_tagged=Y own_title_tag_exact=N "
                     "explained_authors_series=N explained_with_subtitle=N"):
        assert expected in lines
    text = "\n".join(lines).casefold()
    assert str(library.top).casefold() not in text
    words = [w for book in (QUATERMAIN, MARIE, TANAR, PELLUCIDAR) for w in book[0].casefold().split() if len(w) > 3]
    assert words and not any(word in text for word in words)
