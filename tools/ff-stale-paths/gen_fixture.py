#!/usr/bin/env python3
"""Seed a stale-path library fixture for a throwaway Listenarr instance.

Takes a migrated .620-schema DB (no audiobooks) and a scratch ROOT-A directory and
creates 50 library books from the corpus plus 2 unattributed corpus files:

- 2 books whose AudiobookFiles.Path resolves on disk ("fresh").
- 48 books whose stored path does not exist; the real file sits elsewhere in ROOT-A
  under a renamed name. Even-indexed stale books keep their folder and only the file
  name changed; odd-indexed ones moved to a different folder. Two stale books are
  multi-file (two parts). Book 4 is mixed: part 1 still resolves, part 2 was renamed in
  the same folder. Book 6 has an empty BasePath (a per-book scan then covers the root).

Modes:
  --phase stored  create audio files at the STORED paths (pre-rename state); run
                  `--phase rename` later to move them (mv keeps inodes, like a bulk
                  rename on one filesystem).
  --phase real    create audio files directly at the REAL (renamed) paths.
  --phase rename  move files from stored to real paths (requires a prior `stored`).

Every title/author/ASIN is an exact corpus.json value.
"""
from __future__ import annotations

import argparse
import json
import os
import sqlite3
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

LIBRARY_BOOKS = 50
FRESH_INDEXES = {0, 1}
MULTI_FILE_INDEXES = {2, 3, 4}
MIXED_INDEXES = {4}
EMPTY_BASEPATH_INDEXES = {6}
EXTRA_UNATTRIBUTED = 2
CREATED_AT = "2026-08-01 12:00:00"


@dataclass
class Book:
    """One corpus book and its stored/real file layout."""

    index: int
    asin: str
    title: str
    author: str
    series: str | None
    series_position: str | None
    year: str
    narrators: list[str]
    stored: list[Path] = field(default_factory=list)
    real: list[Path] = field(default_factory=list)

    @property
    def stale(self) -> bool:
        return self.index not in FRESH_INDEXES


def safe(segment: str) -> str:
    """Make a corpus string usable as one path segment (slash only)."""
    return segment.replace("/", "-").strip()


def load_books(corpus_path: Path) -> list[Book]:
    """Pick the first unique-title corpus books (library + extras)."""
    books_raw = json.loads(corpus_path.read_text(encoding="utf-8"))["books"]
    seen: set[str] = set()
    books: list[Book] = []
    for raw in books_raw:
        key = raw["title"].casefold()
        if key in seen or not raw.get("authors"):
            continue
        seen.add(key)
        books.append(Book(
            index=len(books),
            asin=raw["asin"],
            title=raw["title"],
            author=raw["authors"][0],
            series=raw.get("series"),
            series_position=raw.get("series_position"),
            year=(raw.get("release_date") or "2000")[:4],
            narrators=raw.get("narrators") or [],
        ))
        if len(books) == LIBRARY_BOOKS + EXTRA_UNATTRIBUTED:
            break
    return books


def layout(book: Book, root: Path) -> None:
    """Fill stored and real paths for a book."""
    author, title = safe(book.author), safe(book.title)
    stored_dir = root / author / title
    parts = 2 if book.index in MULTI_FILE_INDEXES else 1
    if parts == 1:
        stored_names = [f"{title}.m4b"]
    else:
        stored_names = [f"{title}-{n:02d}.mp3" for n in range(1, parts + 1)]
    book.stored = [stored_dir / name for name in stored_names]
    if not book.stale or book.index >= LIBRARY_BOOKS:
        book.real = list(book.stored)
        return
    if book.index % 2 == 0:
        real_dir = stored_dir
        real_names = [name.replace(title, f"{title} - {author}", 1) for name in stored_names]
        if book.index in MIXED_INDEXES:
            real_names[0] = stored_names[0]
    else:
        series = safe(book.series) if book.series else "Standalone"
        real_dir = root / author / series / f"{title} ({book.year})"
        real_names = list(stored_names)
    book.real = [real_dir / name for name in real_names]


def make_audio(target: Path, book: Book, part: int) -> None:
    """Write a short tagged audio file with content unique to the book/part."""
    target.parent.mkdir(parents=True, exist_ok=True)
    frequency = 200 + book.index * 7 + part
    seconds = 2 + (book.index % 5)
    codec = ["-c:a", "aac", "-b:a", "32k", "-f", "mp4"] if target.suffix == ".m4b" else ["-c:a", "libmp3lame", "-b:a", "32k"]
    subprocess.run(
        ["ffmpeg", "-nostdin", "-loglevel", "error", "-y", "-f", "lavfi",
         "-i", f"sine=frequency={frequency}:duration={seconds}",
         "-metadata", f"title={book.title}", "-metadata", f"artist={book.author}",
         "-metadata", f"album={book.title}", "-metadata", f"album_artist={book.author}",
         "-metadata", f"track={part}", *codec, str(target)],
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


def seed_db(db: Path, root: Path, books: list[Book], audio_paths: dict[Path, Path]) -> None:
    """Insert root folder, audiobooks and file rows (stored paths) into the DB."""
    con = sqlite3.connect(db)
    con.execute("PRAGMA foreign_keys=ON")
    cur = con.cursor()
    cur.execute("UPDATE RootFolders SET Path=?, DirectoryObjectIdentity=NULL, "
                "DirectoryObjectIdentityUnavailableReason=NULL, DirectoryObjectIdentityVersion=NULL, "
                "PathIdentityKey=NULL, PathIdentityState='Unavailable' WHERE Id=1", (str(root),))
    cur.execute("UPDATE ApplicationSettings SET OutputPath=?, RecycleBinPath='' WHERE Id=1", (str(root) + "/",))
    for book in books[:LIBRARY_BOOKS]:
        first = book.stored[0]
        total = sum(audio_paths[p].stat().st_size for p in book.stored)
        cur.execute(
            "INSERT INTO Audiobooks (Title, Asin, Authors, Narrators, Series, SeriesNumber, PublishYear, "
            "Language, Explicit, Abridged, Monitored, FilePath, FileSize, Quality, QualityProfileId, BasePath) "
            "VALUES (?,?,?,?,?,?,?,?,0,0,1,?,?,?,1,?)",
            (book.title, book.asin, json.dumps([book.author]), json.dumps(book.narrators), book.series,
             book.series_position, book.year, "english", str(first), total,
             "M4B" if first.suffix == ".m4b" else "MP3",
             None if book.index in EMPTY_BASEPATH_INDEXES else str(first.parent)),
        )
        audiobook_id = cur.lastrowid
        for stored in book.stored:
            physical = audio_paths[stored]
            duration, codec, bitrate = probe(physical)
            fmt = "m4b" if stored.suffix == ".m4b" else "mp3"
            cur.execute(
                "INSERT INTO AudiobookFiles (AudiobookId, Path, Size, DurationSeconds, Format, Container, Codec, "
                "Bitrate, SampleRate, Channels, CreatedAt, Source) VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
                (audiobook_id, str(stored), physical.stat().st_size, duration, fmt,
                 "mp4" if fmt == "m4b" else "mp3", codec, bitrate, 44100, 1, CREATED_AT, "import"),
            )
    con.commit()
    con.close()


def main() -> int:
    """CLI entry point."""
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--corpus", required=True, type=Path)
    parser.add_argument("--root", required=True, type=Path, help="scratch ROOT-A (absolute)")
    parser.add_argument("--db", type=Path, help="DB to seed (phases stored/real)")
    parser.add_argument("--phase", required=True, choices=["stored", "real", "rename"])
    parser.add_argument("--layout-out", type=Path, help="write the stored/real layout as JSON")
    args = parser.parse_args()

    root = args.root.resolve()
    books = load_books(args.corpus)
    for book in books:
        layout(book, root)

    if args.phase == "rename":
        moved = 0
        for book in books[:LIBRARY_BOOKS]:
            for stored, real in zip(book.stored, book.real):
                if stored != real:
                    real.parent.mkdir(parents=True, exist_ok=True)
                    os.rename(stored, real)
                    moved += 1
            if book.stale and book.index % 2 == 1:
                try:
                    book.stored[0].parent.rmdir()
                except OSError:
                    pass
        print(f"renamed files: {moved}")
        return 0

    if args.db is None:
        parser.error("--db is required for phases stored/real")
    if root.exists() and any(root.iterdir()):
        print(f"ERROR {root} is not empty", file=sys.stderr)
        return 2
    audio_paths: dict[Path, Path] = {}
    for book in books:
        targets = book.stored if args.phase == "stored" else book.real
        for part, (stored, target) in enumerate(zip(book.stored, targets), start=1):
            make_audio(target, book, part)
            audio_paths[stored] = target
    seed_db(args.db, root, books, audio_paths)
    if args.layout_out:
        args.layout_out.write_text(json.dumps(
            [{"index": b.index, "asin": b.asin, "title": b.title, "library": b.index < LIBRARY_BOOKS,
              "stale": b.stale and b.index < LIBRARY_BOOKS,
              "stored": [str(p) for p in b.stored], "real": [str(p) for p in b.real]} for b in books],
            indent=1), encoding="utf-8")
    print(f"seeded {LIBRARY_BOOKS} books ({sum(b.stale for b in books[:LIBRARY_BOOKS])} stale), "
          f"{EXTRA_UNATTRIBUTED} unattributed extras, phase={args.phase}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
