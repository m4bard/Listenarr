#!/usr/bin/env python3
"""Seed an adversarial stale-path library fixture for a throwaway Listenarr instance.

Companion to tools/ff-stale-paths/gen_fixture.py: same DB insert shape and the same
`--phase stored|real|rename` model, but the library is a set of small hand-built cases
that each try to trick a stale-path matcher into a wrong link. Takes a migrated
.620-schema DB (no audiobooks) and a scratch ROOT-A directory.

Cases (every row is labelled with its case id in expected.json):

  F    control, stored path live                  C7   old path reused by another book
  C1   duplicate editions, identical bytes        C8   3 equal-size parts, moved, tracks 1..3
  C2   duplicate editions, same size, other audio C8b  parts renumbered on disk, tags 1..3
  C3   same file name, different size             C8c  2 equal-size parts, no numbers, no tags
  C3b  same file name, same size                  C9   symlink decoy next to the real file
  C4   book folder nested inside another's        C10  mixed live/renamed parts + foreign file
  C5   vanished file                              C11  hard-linked file, two names
  C5b  vanished file + same-shape untracked decoy C12  empty BasePath
  C6   plain moved folder

Modes:
  --phase stored  create audio files at the STORED paths (pre-rename state) and seed the DB
  --phase real    create audio files directly at the REAL paths and seed the DB
  --phase rename  move stored -> real (os.rename keeps inodes), delete vanished files, then
                  add the untracked decoy, hard link and symlink (requires a prior `stored`)
  --phase check   verify expected.json against the filesystem (no other inputs needed)

The v1 expectation is for a matcher that can use the file identities recorded before the
rename; v2 is for one that only has the paths and file properties. The two differ only
where identity is the sole way to tell files apart.

Every title/author/ASIN is an exact corpus.json value.

usage: gen_adversarial.py --phase stored|real --corpus CORPUS --root ROOT-A --db DB
                          --layout-out layout.json --expected-out expected.json
       gen_adversarial.py --phase rename --corpus CORPUS --root ROOT-A
       gen_adversarial.py --phase check --expected expected.json
"""
from __future__ import annotations

import argparse
import contextlib
import hashlib
import json
import os
import shutil
import sqlite3
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

LINK = "link"
LEAVE_OUT = "leave-out"
UNTOUCHED = "untouched"
CREATED_AT = "2026-08-01 12:00:00"
BASELINE_PAD = 1
MAX_PAD_ROUNDS = 4

Expect = tuple[str, str]
CORPUS_BOOK = dict[str, Any]


@dataclass
class FileSpec:
    """One audio file: where its row says it is, where it really is, and what is in it."""

    key: str
    case: str
    ext: str
    frequency: int
    seconds: int
    title: str
    artist: str
    album: str
    expect: Expect
    stored: Path | None = None
    real: Path | None = None
    track: int | None = None
    size_group: str | None = None
    copy_of: str | None = None
    hardlinks: list[Path] = field(default_factory=list)
    symlinks: list[Path] = field(default_factory=list)
    acceptable: list[str] = field(default_factory=list)
    pad: int = BASELINE_PAD
    row_id: int = 0

    @property
    def tracked(self) -> bool:
        """True when the DB has a row for this file."""
        return self.stored is not None


@dataclass
class BookSpec:
    """One library audiobook (a corpus book) and its file rows."""

    case: str
    role: str
    raw: CORPUS_BOOK
    base_path: Path | None
    files: list[FileSpec] = field(default_factory=list)
    book_id: int = 0

    @property
    def title(self) -> str:
        """Corpus title."""
        return str(self.raw["title"])

    @property
    def author(self) -> str:
        """First corpus author."""
        return str(self.raw["authors"][0])

    @property
    def year(self) -> str:
        """Release year, as the DB PublishYear."""
        return str(self.raw.get("release_date") or "2000")[:4]


@dataclass
class Plan:
    """All books plus the untracked files and the cross-file assertions."""

    books: list[BookSpec] = field(default_factory=list)
    untracked: list[FileSpec] = field(default_factory=list)
    differ_in_size: list[tuple[str, str]] = field(default_factory=list)
    same_shape: list[tuple[str, str]] = field(default_factory=list)

    def all_files(self) -> list[FileSpec]:
        """Tracked and untracked files in plan order."""
        return [f for b in self.books for f in b.files] + self.untracked


def safe(segment: str) -> str:
    """Make a corpus string usable as one path segment (slash only)."""
    return segment.replace("/", "-").strip()


class Planner:
    """Builds the case plan from the corpus."""

    def __init__(self, corpus_path: Path, root: Path) -> None:
        self.raws: list[CORPUS_BOOK] = [
            b for b in json.loads(corpus_path.read_text(encoding="utf-8"))["books"] if b.get("authors")
        ]
        self.root = root
        self.plan = Plan()
        self.counter = 0

    def pick(self, title: str, nth: int = 0) -> CORPUS_BOOK:
        """Return the nth corpus row whose title matches exactly."""
        rows = [b for b in self.raws if b["title"] == title]
        if len(rows) <= nth:
            raise SystemExit(f"ERROR corpus has no row #{nth} titled {title!r}")
        return rows[nth]

    def pick_duplicates(self, title_casefolded: str) -> list[CORPUS_BOOK]:
        """Return the corpus rows sharing one case-folded title (editions)."""
        rows = [b for b in self.raws if b["title"].casefold() == title_casefolded]
        if len(rows) < 2:
            raise SystemExit(f"ERROR corpus has no duplicate title {title_casefolded!r}")
        return rows

    def stored_dir(self, book: BookSpec) -> Path:
        """Default stored folder: ROOT/Author/Title."""
        return self.root / safe(book.author) / safe(book.title)

    def moved_dir(self, raw: CORPUS_BOOK) -> Path:
        """Return the moved folder: ROOT/Author/Series-or-Standalone/Title (year)."""
        series = safe(raw["series"]) if raw.get("series") else "Standalone"
        year = str(raw.get("release_date") or "2000")[:4]
        return self.root / safe(raw["authors"][0]) / series / f"{safe(raw['title'])} ({year})"

    def book(self, case: str, role: str, raw: CORPUS_BOOK, base: str | Path | None = "default") -> BookSpec:
        """Start a library book; base is the BasePath (default stored folder, None for empty)."""
        spec = BookSpec(case, role, raw, None)
        if base == "default":
            spec.base_path = self.stored_dir(spec)
        elif isinstance(base, Path):
            spec.base_path = base
        self.plan.books.append(spec)
        return spec

    def add(self, book: BookSpec, key: str, ext: str, stored: Path | None, real: Path | None, expect: Expect,
            seconds: int = 2, *, title: str | None = None, album: str | None = None, track: int | None = None,
            size_group: str | None = None, copy_of: str | None = None, hardlinks: list[Path] | None = None,
            symlinks: list[Path] | None = None, acceptable: list[str] | None = None) -> FileSpec:
        """Append a file spec to a book; every file gets its own sine frequency."""
        self.counter += 1
        spec = FileSpec(
            key=key, case=book.case, ext=ext, frequency=200 + self.counter * 7, seconds=seconds,
            title=title or book.title, artist=book.author, album=album or book.title, expect=expect,
            stored=stored, real=real, track=track, size_group=size_group, copy_of=copy_of,
            hardlinks=hardlinks or [], symlinks=symlinks or [], acceptable=acceptable or [],
        )
        book.files.append(spec)
        return spec

    def build(self) -> Plan:
        """Build every case."""
        self.control()
        self.duplicate_editions()
        self.same_name()
        self.nested()
        self.vanished()
        self.moved_and_reused()
        self.multipart()
        self.links_and_mixed()
        self.empty_base_path()
        return self.plan

    def control(self) -> None:
        """F: two books whose stored paths are live."""
        for n, title in enumerate(["Jane Eyre", "Wuthering Heights"], start=1):
            book = self.book("F", f"F{n}", self.pick(title))
            path = self.stored_dir(book) / f"{safe(book.title)}.m4b"
            self.add(book, f"F{n}", ".m4b", path, path, (UNTOUCHED, UNTOUCHED))

    def duplicate_editions(self) -> None:
        """C1 and C2: two editions of one title."""
        first, second = self.pick_duplicates("die verwandlung")[:2]
        self._edition_pair("C1", ".m4b", first, second, identical=True, seconds=2, same_size_group=None)
        first, second = self.pick_duplicates("don quijote de la mancha")[:2]
        self._edition_pair("C2", ".mp3", first, second, identical=False, seconds=3, same_size_group="C2")

    def _edition_pair(self, case: str, ext: str, first: CORPUS_BOOK, second: CORPUS_BOOK, identical: bool,
                      seconds: int, same_size_group: str | None) -> None:
        if str(first["release_date"])[:4] == str(second["release_date"])[:4]:
            raise SystemExit(f"ERROR {case} editions share a release year, moved folders would collide")
        first_key = ""
        for n, raw in enumerate([first, second], start=1):
            stored = (self.root / safe(raw["authors"][0]) / f"{safe(raw['title'])} [{raw['asin']}]"
                      / f"{safe(first['title'])}{ext}")
            real = self.moved_dir(raw) / f"{safe(first['title'])}{ext}"
            book = self.book(case, f"{case}-E{n}", raw, base=stored.parent)
            key = f"{case}-E{n}"
            self.add(book, key, ext, stored, real, (LINK, LEAVE_OUT), seconds=seconds, title=first["title"],
                     album=first["title"] if identical else f"{first['title']} [{raw['asin']}]",
                     size_group=same_size_group, copy_of=first_key if identical and n == 2 else None)
            first_key = first_key or key

    def same_name(self) -> None:
        """C3 and C3b: different books with the same real file name."""
        for n, (title, seconds) in enumerate([("Agnes Grey", 2), ("The Turn of the Screw", 4)], start=1):
            book = self.book("C3", f"C3-{n}", self.pick(title))
            self.add(book, f"C3-{n}", ".m4b", self.stored_dir(book) / f"{safe(book.title)}.m4b",
                     self.moved_dir(book.raw) / "audiobook.m4b", (LINK, LINK), seconds=seconds)
        self.plan.differ_in_size.append(("C3-1", "C3-2"))
        for n, title in enumerate(["Ghost Stories of an Antiquary", "Middlemarch"], start=1):
            book = self.book("C3b", f"C3b-{n}", self.pick(title))
            self.add(book, f"C3b-{n}", ".mp3", self.stored_dir(book) / f"{safe(book.title)}.mp3",
                     self.moved_dir(book.raw) / "part01.mp3", (LINK, LEAVE_OUT), seconds=3, size_group="C3b")

    def nested(self) -> None:
        """C4: one book's folder nested inside another's."""
        outer = self.pick("Can You Forgive Her?")
        inner = self.pick("Is He Popenjoy?")
        if outer["authors"][0] != inner["authors"][0] or not outer.get("series"):
            raise SystemExit("ERROR C4 needs two books by one author, the first in a series")
        series_dir = self.root / safe(outer["authors"][0]) / safe(outer["series"])
        n1 = self.book("C4", "N1", outer, base=series_dir)
        self.add(n1, "C4-N1", ".m4b", series_dir / f"{safe(outer['title'])}.m4b",
                 series_dir / f"{safe(outer['series'])}.m4b", (LINK, LINK))
        n2_dir = series_dir / safe(inner["title"])
        n2 = self.book("C4", "N2", inner, base=n2_dir)
        self.add(n2, "C4-N2", ".m4b", n2_dir / f"{safe(inner['title'])}.m4b",
                 n2_dir / f"{safe(inner['title'])} - {safe(inner['authors'][0])}.m4b", (LINK, LINK))

    def vanished(self) -> None:
        """C5 and C5b: deleted files, one with a same-shape untracked decoy."""
        gone = self.book("C5", "V1", self.pick("Dracula"))
        self.add(gone, "C5-V1", ".m4b", self.stored_dir(gone) / f"{safe(gone.title)}.m4b", None,
                 (LEAVE_OUT, LEAVE_OUT))
        victim = self.book("C5b", "V2", self.pick("Frankenstein"))
        self.add(victim, "C5b-V2", ".mp3", self.stored_dir(victim) / f"{safe(victim.title)}.mp3", None,
                 (LEAVE_OUT, LEAVE_OUT), seconds=3, size_group="C5b")
        other = self.pick("Beowulf")
        self.counter += 1
        decoy = FileSpec(
            key="C5b-decoy", case="C5b", ext=".mp3", frequency=200 + self.counter * 7, seconds=3,
            title=other["title"], artist=other["authors"][0], album=other["title"], expect=(LEAVE_OUT, LEAVE_OUT),
            real=self.root / "Misc" / safe(other["title"]) / f"{safe(other['title'])}.mp3", size_group="C5b",
        )
        self.plan.untracked.append(decoy)
        self.plan.same_shape.append(("C5b-V2", "C5b-decoy"))

    def moved_and_reused(self) -> None:
        """C6 and C7: a plain move, and an old path reused by another book."""
        plain = self.book("C6", "P", self.pick("Treasure Island"))
        self.add(plain, "C6-P", ".m4b", self.stored_dir(plain) / f"{safe(plain.title)}.m4b",
                 self.moved_dir(plain.raw) / f"{safe(plain.title)}.m4b", (LINK, LINK))
        s1 = self.book("C7", "S1", self.pick("Greenmantle"))
        s1_path = self.stored_dir(s1) / f"{safe(s1.title)}.m4b"
        self.add(s1, "C7-S1", ".m4b", s1_path, self.moved_dir(s1.raw) / f"{safe(s1.title)}.m4b",
                 (UNTOUCHED, UNTOUCHED), seconds=2)
        s2 = self.book("C7", "S2", self.pick("The Thirty-Nine Steps"))
        self.add(s2, "C7-S2", ".m4b", self.stored_dir(s2) / f"{safe(s2.title)}.m4b", s1_path,
                 (LEAVE_OUT, LEAVE_OUT), seconds=4)
        self.plan.differ_in_size.append(("C7-S1", "C7-S2"))

    def multipart(self) -> None:
        """C8, C8b and C8c: equal-size multi-part books."""
        for case, title, shift, tagged, expect, acceptable in [
            ("C8", "Crime and Punishment", 0, True, (LINK, LINK), []),
            ("C8b", "The Odyssey", 1, True, (LINK, LEAVE_OUT), [LINK, LEAVE_OUT]),
        ]:
            book = self.book(case, case, self.pick(title))
            self._parts(book, 3, shift, tagged, expect, acceptable)
        book = self.book("C8c", "C8c", self.pick("Essays"))
        stored = [self.stored_dir(book) / f"{safe(book.title)}-{n:02d}.mp3" for n in (1, 2)]
        for n, (path, name) in enumerate(zip(stored, ["a.mp3", "b.mp3"], strict=True), start=1):
            self.add(book, f"C8c-{n}", ".mp3", path, self.moved_dir(book.raw) / name, (LINK, LEAVE_OUT),
                     seconds=2, size_group="C8c")

    def _parts(self, book: BookSpec, parts: int, shift: int, tagged: bool, expect: Expect,
               acceptable: list[str]) -> None:
        title = safe(book.title)
        for n in range(1, parts + 1):
            self.add(book, f"{book.case}-{n}", ".mp3", self.stored_dir(book) / f"{title}-{n:02d}.mp3",
                     self.moved_dir(book.raw) / f"{title}-{n + shift:02d}.mp3", expect, seconds=2,
                     track=n if tagged else None, size_group=book.case, acceptable=list(acceptable))

    def links_and_mixed(self) -> None:
        """C9, C10 and C11: symlink decoy, mixed live/renamed with a foreign file, hard link."""
        link = self.book("C9", "L", self.pick("Ozymandias"))
        real = self.moved_dir(link.raw) / f"{safe(link.title)}.m4b"
        self.add(link, "C9-L", ".m4b", self.stored_dir(link) / f"{safe(link.title)}.m4b", real, (LINK, LINK),
                 symlinks=[self.root / "Links" / f"{safe(link.title)}.m4b"])

        mixed = self.book("C10", "M", self.pick("Anna of the Five Towns"))
        title, author = safe(mixed.title), safe(mixed.author)
        folder = self.stored_dir(mixed)
        live = folder / f"{title}-01.mp3"
        self.add(mixed, "C10-M1", ".mp3", live, live, (UNTOUCHED, UNTOUCHED), track=1, size_group="C10")
        self.add(mixed, "C10-M2", ".mp3", folder / f"{title}-02.mp3",
                 folder / f"{title} - {author}-02.mp3", (LINK, LINK), track=2, size_group="C10")
        foreign = self.book("C10", "X", self.pick("She And Allan"))
        self.add(foreign, "C10-X", ".m4b", self.stored_dir(foreign) / f"{safe(foreign.title)}.m4b",
                 folder / "Extras" / f"{safe(foreign.title)}.m4b", (LINK, LINK))

        hard = self.book("C11", "H", self.pick("Stalky and Co."))
        first = self.moved_dir(hard.raw) / f"{safe(hard.title)}.m4b"
        self.add(hard, "C11-H", ".m4b", self.stored_dir(hard) / f"{safe(hard.title)}.m4b", first,
                 (LEAVE_OUT, LEAVE_OUT),
                 hardlinks=[self.root / safe(hard.author) / "Alt Copy" / f"{safe(hard.title)}.m4b"])

    def empty_base_path(self) -> None:
        """C12: same-folder rename with a NULL BasePath."""
        book = self.book("C12", "E", self.pick("The Four Million"), base=None)
        folder = self.stored_dir(book)
        self.add(book, "C12-E", ".m4b", folder / f"{safe(book.title)}.m4b",
                 folder / f"{safe(book.title)} - {safe(book.author)}.m4b", (LINK, LINK))


def render(spec: FileSpec, target: Path) -> None:
    """Write a short tagged audio file; CBR so equal duration gives equal size."""
    target.parent.mkdir(parents=True, exist_ok=True)
    is_m4b = spec.ext == ".m4b"
    codec = (["-c:a", "aac", "-b:a", "32k", "-f", "mp4"] if is_m4b
             else ["-c:a", "libmp3lame", "-b:a", "32k", "-id3v2_version", "3"])
    tags = {"title": spec.title, "artist": spec.artist, "album": spec.album, "album_artist": spec.artist}
    if spec.track is not None:
        tags["track"] = str(spec.track)
    if spec.size_group is not None:
        tags["comment"] = "x" * spec.pad
    metadata = [arg for key, value in tags.items() for arg in ("-metadata", f"{key}={value}")]
    subprocess.run(
        ["ffmpeg", "-nostdin", "-loglevel", "error", "-y", "-f", "lavfi",
         "-i", f"sine=frequency={spec.frequency}:duration={spec.seconds}", "-ar", "44100", "-ac", "1",
         *metadata, *codec, str(target)],
        check=True,
    )


def probe(path: Path) -> tuple[float, str, int]:
    """Return (duration seconds, codec name, bitrate) via ffprobe."""
    out = subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration,bit_rate:stream=codec_name",
         "-of", "json", str(path)],
        check=True, capture_output=True, text=True,
    ).stdout
    data = json.loads(out)
    return (float(data["format"]["duration"]), data["streams"][0]["codec_name"],
            int(data["format"].get("bit_rate") or 0))


def render_all(plan: Plan, scratch: Path) -> dict[str, Path]:
    """Render every file once into scratch, equalising sizes inside each size group."""
    files = plan.all_files()
    rendered = {f.key: scratch / f"{f.key}{f.ext}" for f in files}
    for spec in files:
        if spec.copy_of is None:
            render(spec, rendered[spec.key])
    groups: dict[str, list[FileSpec]] = {}
    for spec in files:
        if spec.size_group is not None:
            groups.setdefault(spec.size_group, []).append(spec)
    for name, members in groups.items():
        if any(m.ext != ".mp3" for m in members):
            raise SystemExit(f"ERROR size group {name} has a non-mp3 member, only ID3 padding is supported")
        for _ in range(MAX_PAD_ROUNDS):
            sizes = {m.key: rendered[m.key].stat().st_size for m in members}
            if len(set(sizes.values())) == 1:
                break
            largest = max(sizes.values())
            for member in members:
                if sizes[member.key] < largest:
                    member.pad += largest - sizes[member.key]
                    render(member, rendered[member.key])
        sizes = {m.key: rendered[m.key].stat().st_size for m in members}
        if len(set(sizes.values())) != 1:
            raise SystemExit(f"ERROR size group {name} could not be equalised: {sizes}")
        print(f"equal-size group {name}: {len(members)} files, {next(iter(sizes.values()))} bytes each")
    for spec in files:
        if spec.copy_of is not None:
            shutil.copyfile(rendered[spec.copy_of], rendered[spec.key])
    check_plan_assertions(plan, rendered)
    return rendered


def check_plan_assertions(plan: Plan, rendered: dict[str, Path]) -> None:
    """Fail loudly if the rendered files do not have the properties the cases rely on."""
    for left, right in plan.differ_in_size:
        if rendered[left].stat().st_size == rendered[right].stat().st_size:
            raise SystemExit(f"ERROR {left} and {right} must differ in size")
    for left, right in plan.same_shape:
        shape = []
        for key in (left, right):
            duration, codec, bitrate = probe(rendered[key])
            shape.append((round(duration, 3), codec, bitrate, rendered[key].stat().st_size))
        if shape[0] != shape[1]:
            raise SystemExit(f"ERROR {left} and {right} must share size/duration/codec/bitrate: {shape}")
    digests: dict[str, str] = {}
    for spec in plan.all_files():
        if spec.copy_of is not None:
            continue
        digest = hashlib.sha256(rendered[spec.key].read_bytes()).hexdigest()
        if digest in digests:
            raise SystemExit(f"ERROR {spec.key} duplicates the bytes of {digests[digest]}")
        digests[digest] = spec.key
    identical = [s for s in plan.all_files() if s.copy_of is not None]
    for spec in identical:
        if rendered[spec.key].read_bytes() != rendered[spec.copy_of or ""].read_bytes():
            raise SystemExit(f"ERROR {spec.key} must be byte-identical to {spec.copy_of}")


def place(source: Path, target: Path) -> None:
    """Copy a rendered file into the library as its own inode."""
    if target.exists() or target.is_symlink():
        raise SystemExit(f"ERROR {target} already exists")
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)


def add_links(spec: FileSpec) -> None:
    """Create the hard link and symlink names for a file at its real path."""
    if spec.real is None:
        return
    for name in spec.hardlinks:
        name.parent.mkdir(parents=True, exist_ok=True)
        os.link(spec.real, name)
    for name in spec.symlinks:
        name.parent.mkdir(parents=True, exist_ok=True)
        os.symlink(spec.real, name)


def seed_db(db: Path, root: Path, plan: Plan, rendered: dict[str, Path]) -> None:
    """Insert root folder, audiobooks and file rows (stored paths) into the DB."""
    con = sqlite3.connect(db)
    con.execute("PRAGMA foreign_keys=ON")
    cur = con.cursor()
    cur.execute("UPDATE RootFolders SET Path=?, DirectoryObjectIdentity=NULL, "
                "DirectoryObjectIdentityUnavailableReason=NULL, DirectoryObjectIdentityVersion=NULL, "
                "PathIdentityKey=NULL, PathIdentityState='Unavailable' WHERE Id=1", (str(root),))
    cur.execute("UPDATE ApplicationSettings SET OutputPath=?, RecycleBinPath='' WHERE Id=1", (str(root) + "/",))
    for book in plan.books:
        first = book.files[0]
        total = sum(rendered[f.key].stat().st_size for f in book.files)
        cur.execute(
            "INSERT INTO Audiobooks (Title, Asin, Authors, Narrators, Series, SeriesNumber, PublishYear, "
            "Language, Explicit, Abridged, Monitored, FilePath, FileSize, Quality, QualityProfileId, BasePath) "
            "VALUES (?,?,?,?,?,?,?,?,0,0,1,?,?,?,1,?)",
            (book.title, book.raw["asin"], json.dumps([book.author]), json.dumps(book.raw.get("narrators") or []),
             book.raw.get("series"), book.raw.get("series_position"), book.year, "english", str(first.stored),
             total, "M4B" if first.ext == ".m4b" else "MP3",
             None if book.base_path is None else str(book.base_path)),
        )
        book.book_id = cur.lastrowid or 0
        for spec in book.files:
            duration, codec, bitrate = probe(rendered[spec.key])
            fmt = spec.ext.lstrip(".")
            cur.execute(
                "INSERT INTO AudiobookFiles (AudiobookId, Path, Size, DurationSeconds, Format, Container, Codec, "
                "Bitrate, SampleRate, Channels, CreatedAt, Source) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
                (book.book_id, str(spec.stored), rendered[spec.key].stat().st_size, duration, fmt,
                 "mp4" if fmt == "m4b" else "mp3", codec, bitrate, 44100, 1, CREATED_AT, "import"),
            )
            spec.row_id = cur.lastrowid or 0
    con.commit()
    con.close()


def write_outputs(plan: Plan, variant: str, layout_out: Path | None, expected_out: Path | None) -> None:
    """Write layout.json (books, rows, untracked files) and expected.json (one entry per row)."""
    variant_index = 0 if variant == "v1" else 1
    expected = []
    for book in plan.books:
        for spec in book.files:
            entry: dict[str, Any] = {
                "row_id": spec.row_id, "audiobook_id": book.book_id, "case": spec.case, "role": book.role,
                "key": spec.key, "expect": spec.expect[variant_index],
                "truth_path": None if spec.real is None else str(spec.real), "stored_path": str(spec.stored),
            }
            if spec.acceptable:
                entry["acceptable"] = spec.acceptable
            expected.append(entry)
    if expected_out:
        expected_out.write_text(json.dumps(expected, indent=1), encoding="utf-8")
    if layout_out:
        layout = {
            "variant": variant,
            "books": [{"book_id": b.book_id, "case": b.case, "role": b.role, "asin": b.raw["asin"],
                       "title": b.title, "base_path": None if b.base_path is None else str(b.base_path),
                       "files": [{"key": f.key, "row_id": f.row_id, "stored": str(f.stored),
                                  "real": None if f.real is None else str(f.real),
                                  "hardlinks": [str(p) for p in f.hardlinks],
                                  "symlinks": [str(p) for p in f.symlinks]} for f in b.files]}
                      for b in plan.books],
            "untracked": [{"key": f.key, "case": f.case, "path": str(f.real)} for f in plan.untracked],
            "named": {f"{b.case}-{b.role}": b.book_id for b in plan.books if b.case == "C10"},
        }
        layout_out.write_text(json.dumps(layout, indent=1), encoding="utf-8")


def rename_phase(plan: Plan, scratch: Path) -> None:
    """Move stored -> real (inodes kept), drop vanished files, then add untracked files and links."""
    moved = removed = 0
    for spec in (f for b in plan.books for f in b.files):
        assert spec.stored is not None
        if spec.real is None:
            spec.stored.unlink()
            removed += 1
        elif spec.stored != spec.real:
            if spec.real.exists():
                raise SystemExit(f"ERROR rename target {spec.real} already exists")
            spec.real.parent.mkdir(parents=True, exist_ok=True)
            os.rename(spec.stored, spec.real)
            moved += 1
        with contextlib.suppress(OSError):
            spec.stored.parent.rmdir()
    for spec in plan.untracked:
        assert spec.real is not None
        place(scratch / f"{spec.key}{spec.ext}", spec.real)
    for spec in plan.all_files():
        add_links(spec)
    shutil.rmtree(scratch)
    print(f"renamed files: {moved}, removed: {removed}, untracked added: {len(plan.untracked)}")


def check_expected(expected_path: Path) -> int:
    """Verify expected.json against the filesystem; return the number of problems."""
    problems: list[str] = []
    entries = json.loads(expected_path.read_text(encoding="utf-8"))
    counts: dict[str, int] = {}
    for entry in entries:
        counts[entry["expect"]] = counts.get(entry["expect"], 0) + 1
        stored, truth = Path(entry["stored_path"]), entry["truth_path"]
        label = f"row {entry['row_id']} {entry['key']}"
        if truth is None:
            if os.path.lexists(stored):
                problems.append(f"{label}: vanished but stored path exists")
            continue
        if not Path(truth).is_file() or Path(truth).is_symlink():
            problems.append(f"{label}: truth_path is not a regular file")
        if entry["expect"] == LINK and os.path.lexists(stored):
            problems.append(f"{label}: link row but stored path exists")
        if entry["expect"] == UNTOUCHED and not stored.exists():
            problems.append(f"{label}: untouched row but stored path is missing")
        if entry["case"] == "C11" and Path(truth).stat().st_nlink != 2:
            problems.append(f"{label}: expected two hard-link names")
    for problem in problems:
        print(f"CHECK FAIL {problem}")
    print(f"check: {len(entries)} rows {counts}, problems={len(problems)}")
    return len(problems)


def main() -> int:
    """CLI entry point."""
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--phase", required=True, choices=["stored", "real", "rename", "check"])
    parser.add_argument("--corpus", type=Path)
    parser.add_argument("--root", type=Path, help="scratch ROOT-A (absolute)")
    parser.add_argument("--db", type=Path, help="DB to seed (phases stored/real)")
    parser.add_argument("--layout-out", type=Path)
    parser.add_argument("--expected-out", type=Path)
    parser.add_argument("--expected", type=Path, help="expected.json to verify (phase check)")
    args = parser.parse_args()

    if args.phase == "check":
        if args.expected is None:
            parser.error("--expected is required for phase check")
        return 1 if check_expected(args.expected) else 0
    if args.corpus is None or args.root is None:
        parser.error("--corpus and --root are required")

    root = args.root.resolve()
    scratch = root.parent / "scratch"
    plan = Planner(args.corpus, root).build()

    if args.phase == "rename":
        rename_phase(plan, scratch)
        return 0

    if args.db is None:
        parser.error("--db is required for phases stored/real")
    if (root.exists() and any(root.iterdir())) or scratch.exists():
        print(f"ERROR {root} or {scratch} is not empty", file=sys.stderr)
        return 2
    rendered = render_all(plan, scratch)
    for book in plan.books:
        for spec in book.files:
            target = spec.stored if args.phase == "stored" else spec.real
            if target is not None:
                place(rendered[spec.key], target)
    if args.phase == "real":
        for spec in plan.untracked:
            assert spec.real is not None
            place(rendered[spec.key], spec.real)
        for spec in plan.all_files():
            add_links(spec)
    seed_db(args.db, root, plan, rendered)
    if args.phase == "real":
        shutil.rmtree(scratch)
    write_outputs(plan, "v1" if args.phase == "stored" else "v2", args.layout_out, args.expected_out)
    print(f"seeded {len(plan.books)} books, {sum(len(b.files) for b in plan.books)} file rows, phase={args.phase}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
