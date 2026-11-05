"""Physical identity across a drifted device minor, on fake identities (no real files, no ffprobe).

Every stored identity, live identity, device and link count here is made up; ``identity.read_live`` and
``media.probe`` are replaced. The stored size is the 64-byte placeholder throughout, so size is no signal.
Titles, authors and ASINs are corpus values.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field

import pytest

from listenarr_relink import identity, media, physical
from listenarr_relink.explain_identity import explain_identity, row_class, row_lines
from listenarr_relink.identity import LiveIdentity
from listenarr_relink.inventory import BookRow, DiskFile, FileRow, Library
from listenarr_relink.matcher import MatchResult, match
from listenarr_relink.paths import PathMap

PRINCESS = ("A Princess of Mars", "Edgar Rice Burroughs", "B008DFUGCQ")
JUNGLE = ("The Jungle Book", "Rudyard Kipling", "B00OQQTXE8")
QUATERMAIN = ("Allan Quatermain", "H. Rider Haggard", "B0096QR7Z0")
PELLUCIDAR = ("Pellucidar", "Edgar Rice Burroughs", "B002V1OVFQ")
LITTLE_WOMEN = ("Little Women", "Louisa May Alcott", "B081B7JM9F")
LITTLE_MEN = ("Little Men", "Louisa May Alcott", "B003750OH4")
ROOT = "/ROOT-A"
OLD_MINOR, LIVE_MINOR, OTHER_MINOR = 0x32, 0x33, 0x40
LIVE_DEV = os.makedev(0, LIVE_MINOR)
PLACEHOLDER = 64
DURATION = 1800.0


def zfs_handle(obj: int, generation: int) -> str:
    """A ZFS short fid as name_to_handle_at returns it: length 10, 6-byte object, 4-byte generation."""
    return f"fh:00000001:0a00{obj.to_bytes(6, 'little').hex()}{generation.to_bytes(4, 'little').hex()}"


def spelled(minor: int, ino: int, generation: int) -> str:
    """The identity .620 persists, here for a made-up device minor, inode and generation."""
    return f"linux-generation:00000000:{minor:08x}:{ino:016x}:{zfs_handle(ino, generation)}"


@dataclass
class Fake:
    """A library of made-up rows and files plus the live identity each file path reports."""

    library: Library = field(default_factory=lambda: Library([ROOT], {}, [], root_devices={ROOT: LIVE_DEV},
                                                             device_pools={LIVE_DEV: "tank"}))
    live: dict[str, LiveIdentity | None] = field(default_factory=dict)
    titles: dict[str, str] = field(default_factory=dict)

    def book(self, book_id: int, book: tuple[str, str, str], *, asin: bool = True) -> str:
        """Add a book; return its old (now gone) folder."""
        title, author, book_asin = book
        folder = f"{ROOT}/{author}/{title} [{book_asin}]"
        self.library.books[book_id] = BookRow({"Id": book_id, "Title": title, "Authors": json.dumps([author]),
                                               "BasePath": folder, "FilePath": None},
                                              {"Asin": book_asin} if asin else {})
        return folder

    def row(self, row_id: int, book_id: int, stored: str | None) -> None:
        """Add a stale row of ``book_id`` (its stored path is gone)."""
        folder = str(self.library.books[book_id].values["BasePath"])
        self.library.files.append(FileRow({
            "Id": row_id, "AudiobookId": book_id, "Path": f"{folder}/{row_id:02d}.mp3", "Size": PLACEHOLDER,
            "DurationSeconds": DURATION, "Codec": "mp3", "SampleRate": 44100, "Channels": 2,
            "PhysicalObjectIdentity": stored, "PhysicalIdentityObservedAtUtc": None, "CanonicalPath": None,
            "PathIdentityState": "Valid", "PathIdentityLookupKey": None, "PathOwnershipKey": None}))

    def file(self, db_path: str, ino: int, generation: int, *, dev: int = LIVE_DEV, nlink: int = 1,
             title: str = PRINCESS[0], readable: bool = True) -> DiskFile:
        """Add an unclaimed audio file under the root with the live identity it reports."""
        disk_file = DiskFile(db_path, db_path, 4_000_000, dev, ino, nlink, 1, 1)
        self.library.disk.append(disk_file)
        self.live[db_path] = LiveIdentity(os.major(dev), os.minor(dev), ino,
                                          frozenset({zfs_handle(ino, generation)})) if readable else None
        self.titles[db_path] = title
        return disk_file

    def run(self, monkeypatch: pytest.MonkeyPatch) -> MatchResult:
        """Match with the fake identities and probes (tags name the file's book)."""
        monkeypatch.setattr(identity, "read_live", lambda path: self.live.get(path))
        monkeypatch.setattr(media, "probe", lambda path: media.Probe(DURATION, "mp3", 44100, 2, 128000,
                                                                     {"title": self.titles[path],
                                                                      "album": self.titles[path]}))
        return match(self.library, PathMap(), workers=1, hash_chosen=False, identity_only=True)


def renamed(book: tuple[str, str, str], name: str = "01 - 01.mp3", bracket: str | None = None) -> str:
    """Where the outside renamer put a book's file: one level deeper, same bracketed id unless overridden."""
    title, author, asin = book
    return f"{ROOT}/{author}/Barsoom/1 - {title} [{bracket or asin}]/{name}"


def outcome(result: MatchResult) -> dict[int, tuple[str, str | None]]:
    """Row id -> (class, reason)."""
    return {d.row.id: (row_class(d), d.reason) for d in result.decisions}


def test_minor_drift_matches_on_the_root_dataset(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    target = fake.file(renamed(PRINCESS), 1234, 77)
    result = fake.run(monkeypatch)
    (decision,) = result.decisions
    assert decision.chosen is not None and decision.chosen.file == target
    assert decision.confidence == "identity" and decision.chosen.device_drift
    assert decision.folder_id == "agrees"
    # The stored identity no longer spells the file's device: APPLY clears it rather than keep a stale one.
    assert result.keep_identity == {1: False}


def test_same_device_match_keeps_the_stored_identity(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(LIVE_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS), 1234, 77)
    result = fake.run(monkeypatch)
    assert outcome(result)[1][0] == "matched" and not result.decisions[0].chosen.device_drift  # type: ignore[union-attr]
    assert result.keep_identity == {1: True}


def test_handle_generation_mismatch_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS), 1234, 78)  # same object number, new generation: the inode was reused
    result = fake.run(monkeypatch)
    assert result.decisions[0].chosen is None
    assert result.decisions[0].identity_class == physical.HANDLE_MISMATCH
    assert outcome(result)[1] == (f"{physical.HANDLE_MISMATCH}:not-identity:no-size",
                                  "no usable stored size and no physical identity match")


def test_handle_object_mismatch_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, f"linux-generation:00000000:{OLD_MINOR:08x}:{1234:016x}:{zfs_handle(999, 77)}")
    fake.file(renamed(PRINCESS), 1234, 77)
    assert fake.run(monkeypatch).decisions[0].identity_class == physical.HANDLE_MISMATCH


def test_generation_only_identity_is_not_matched_across_a_drift(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, f"linux-generation:00000000:{OLD_MINOR:08x}:{1234:016x}:gen:0000004d")
    target = fake.file(renamed(PRINCESS), 1234, 77)
    fake.live[target.host_path] = LiveIdentity(0, LIVE_MINOR, 1234, frozenset({"gen:0000004d"}))
    assert fake.run(monkeypatch).decisions[0].identity_class == physical.HANDLE_MISMATCH


def test_cross_pool_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    other = os.makedev(0, OTHER_MINOR)
    fake.library.device_pools[other] = "backup"
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS), 1234, 77, dev=other)  # a received copy mounted under the root: same handle
    result = fake.run(monkeypatch)
    assert result.decisions[0].chosen is None
    assert outcome(result)[1][0] == physical.CROSS_POOL
    assert "another pool" in str(result.decisions[0].reason)


def test_other_dataset_of_the_same_pool_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    other = os.makedev(0, OTHER_MINOR)
    fake.library.device_pools[other] = "tank"
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS), 1234, 77, dev=other)  # a clone in the same pool: same handle
    result = fake.run(monkeypatch)
    assert result.decisions[0].chosen is None and outcome(result)[1][0] == physical.OTHER_DATASET


def test_stored_device_that_is_another_filesystem_under_the_roots_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    old = os.makedev(0, OLD_MINOR)
    fake.library.device_pools[old] = "backup"
    fake.book(1, PRINCESS)
    fake.book(2, JUNGLE)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS), 1234, 77)
    fake.file(renamed(JUNGLE), 55, 9, dev=old, title=JUNGLE[0])  # the stored device is mounted under the root
    result = fake.run(monkeypatch)
    assert result.decisions[0].chosen is None and outcome(result)[1][0] == physical.CROSS_POOL


def test_a_clone_beside_the_original_is_ambiguous(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    other = os.makedev(0, OTHER_MINOR)
    fake.library.device_pools[other] = "tank"
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS), 1234, 77)
    fake.file(renamed(PRINCESS, "copy.mp3"), 1234, 77, dev=other)
    result = fake.run(monkeypatch)
    assert result.decisions[0].chosen is None and outcome(result)[1][0] == physical.AMBIGUOUS_FILES


def test_an_unreadable_file_with_the_inode_is_ambiguous(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS), 1234, 77)
    fake.file(renamed(PRINCESS, "unreadable.mp3"), 1234, 77, dev=os.makedev(0, OTHER_MINOR), readable=False)
    result = fake.run(monkeypatch)
    assert result.decisions[0].chosen is None and outcome(result)[1][0] == physical.AMBIGUOUS_FILES


def test_a_shared_inode_is_ambiguous(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.book(2, JUNGLE)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.row(2, 2, spelled(LIVE_MINOR, 1234, 90))  # another row, same inode number (its handle differs)
    fake.file(renamed(PRINCESS), 1234, 77)
    result = fake.run(monkeypatch)
    assert all(d.chosen is None for d in result.decisions)
    assert {row: cls for row, (cls, _) in outcome(result).items()} == {1: physical.AMBIGUOUS_ROWS,
                                                                        2: physical.AMBIGUOUS_ROWS}
    assert "ambiguous_rows_whose_file_handle_is_unique 2" in explain_identity(fake.library, result)


def test_a_handle_shared_with_a_live_row_is_ambiguous(monkeypatch: pytest.MonkeyPatch,
                                                       tmp_path: os.PathLike[str]) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    live_file = os.path.join(tmp_path, "held.mp3")
    open(live_file, "wb").close()
    # A live row (its path exists) registered with the very same identity.
    fake.library.files.append(FileRow({**fake.library.files[0].values, "Id": 2, "Path": live_file}))
    fake.file(renamed(PRINCESS), 1234, 77)
    result = fake.run(monkeypatch)
    (decision,) = result.decisions
    assert decision.chosen is None and decision.identity_class == physical.AMBIGUOUS_ROWS


def test_nlink_above_one_refuses(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS), 1234, 77, nlink=2)  # its other name is outside the roots
    result = fake.run(monkeypatch)
    assert result.decisions[0].chosen is None and outcome(result)[1][0] == physical.HARDLINKED
    assert "hardlinked_a_name_elsewhere 1" in explain_identity(fake.library, result)


def test_two_names_of_one_object_refuse(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS), 1234, 77, nlink=2)
    fake.file(renamed(PRINCESS, "02 - 01.mp3"), 1234, 77, nlink=2)
    result = fake.run(monkeypatch)
    assert result.decisions[0].chosen is None and outcome(result)[1][0] == physical.HARDLINKED
    assert "hardlinked_every_name_unclaimed_under_the_roots 1" in explain_identity(fake.library, result)


def test_folder_id_disagreement_is_flagged_and_left_out(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS, bracket=JUNGLE[2]), 1234, 77)  # the object now sits in another book's folder
    result = fake.run(monkeypatch)
    (decision,) = result.decisions
    assert decision.chosen is None and decision.identity_class == physical.FOLDER_ID_DISAGREES
    assert "folder id is not this book's" in str(decision.reason)
    assert [(f.kind, f.row_id) for f in result.flags] == [("folder-id-disagrees", 1)]


def test_folder_id_falls_back_to_the_stored_folder_and_is_case_blind(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS, asin=False)  # no Asin column value: the old folder's bracketed id is the book's
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS, bracket=PRINCESS[2].lower()), 1234, 77)
    (decision,) = fake.run(monkeypatch).decisions
    assert decision.chosen is not None and decision.folder_id == "agrees"


def test_no_folder_id_is_not_a_veto(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(f"{ROOT}/{PRINCESS[1]}/{PRINCESS[0]}/01.mp3", 1234, 77)
    (decision,) = fake.run(monkeypatch).decisions
    assert decision.chosen is not None and decision.folder_id == "unknown"


def test_existing_vetoes_still_apply_under_a_drift(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.book(2, JUNGLE)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.file(renamed(PRINCESS), 1234, 77, title=JUNGLE[0])  # its tags name another library book
    result = fake.run(monkeypatch)
    assert outcome(result)[1][0] == "vetoed:identity-inconsistent-tags-name-another-book"
    real_size = Fake()
    real_size.book(1, PRINCESS)
    real_size.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    real_size.library.files[0].values["Size"] = 3_000_000  # a real stored size that disagrees
    real_size.file(renamed(PRINCESS), 1234, 77)
    assert outcome(real_size.run(monkeypatch))[1][0] == "vetoed:identity-content-changed"


def test_book_is_linked_whole_or_not_at_all_under_a_drift(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    fake.book(1, PRINCESS)
    fake.row(1, 1, spelled(OLD_MINOR, 1234, 77))
    fake.row(2, 1, spelled(OLD_MINOR, 1235, 77))  # its file is gone
    fake.file(renamed(PRINCESS), 1234, 77)
    result = fake.run(monkeypatch)
    assert outcome(result)[1][0] == "vetoed:withheld-book-atomic"
    assert outcome(result)[2][0] == f"{physical.NO_CANDIDATE}:not-identity:no-size"


def test_explain_identity_counts_every_class_without_names_or_paths(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = Fake()
    other = os.makedev(0, OTHER_MINOR)
    fake.library.device_pools[other] = "backup"
    books = (PRINCESS, JUNGLE, QUATERMAIN, PELLUCIDAR, LITTLE_WOMEN, LITTLE_MEN)
    for number, book in enumerate(books, start=1):
        fake.book(number, book)
        fake.row(number, number, spelled(OLD_MINOR, 100 * number, 7))
    fake.row(7, 6, None)
    fake.file(renamed(PRINCESS), 100, 7, title=PRINCESS[0])  # matched across the drift
    fake.file(renamed(JUNGLE), 200, 8, title=JUNGLE[0])  # handle mismatch
    fake.file(renamed(QUATERMAIN), 300, 7, dev=other, title=QUATERMAIN[0])  # cross-pool
    fake.file(renamed(PELLUCIDAR), 400, 7, nlink=3, title=PELLUCIDAR[0])  # hard-linked
    fake.file(renamed(LITTLE_WOMEN, bracket=JUNGLE[2]), 500, 7, title=LITTLE_WOMEN[0])  # folder id disagrees
    result = fake.run(monkeypatch)  # LITTLE_MEN: row 6 finds no file, row 7 has no stored identity
    lines = explain_identity(fake.library, result)
    for expected in ("stale_rows 7", "  matched 1", f"  {physical.HANDLE_MISMATCH} 1", f"  {physical.CROSS_POOL} 1",
                     f"  {physical.HARDLINKED} 1", f"  {physical.FOLDER_ID_DISAGREES} 1",
                     f"  {physical.NO_CANDIDATE} 1", f"  {physical.NO_STORED} 1", f"  {physical.AMBIGUOUS_ROWS} 0",
                     "identity_matched_before_vetoes 1 ", "handle_equal_unclaimed_rows 4 ",
                     "matched_device_drift 1", "matched_same_device 0", "matched_folder_id_agrees 1",
                     "books_whose_every_stale_row_is_matched 1"):
        assert any(line == expected or line.startswith(expected) for line in lines), expected
    classes = lines[lines.index("classes (each stale row in exactly one):") + 1:]
    classes = classes[:next(i for i, line in enumerate(classes) if not line.startswith("  "))]
    assert sum(int(line.split()[-1]) for line in classes) == 7  # every row in exactly one class
    text = "\n".join(lines)
    for word in ("Princess", "Jungle", "Quatermain", "Pellucidar", "Little", "Burroughs", ROOT, "/"):
        assert word not in text
    rows = row_lines(result, with_paths=False)
    assert rows[0].split("\t") == ["row_id", "audiobook_id", "class", "device_drift", "folder_id"]
    assert rows[1].split("\t") == ["1", "1", "matched", "Y", "agrees"]
    assert not any("/" in line for line in rows)
    assert ROOT in "\n".join(row_lines(result, with_paths=True))


def test_mount_pools_names_the_zfs_pool_of_each_device(tmp_path: os.PathLike[str]) -> None:
    table = os.path.join(tmp_path, "mountinfo")
    with open(table, "w", encoding="utf-8") as handle:
        handle.write("36 25 0:51 / /ROOT-A rw,relatime shared:1 - zfs tank/media rw,xattr\n"
                     "37 36 0:64 / /ROOT-A/nested rw - zfs backup/media/copy rw\n"
                     "38 25 8:1 / /boot rw - ext4 /dev/sda1 rw\n"
                     "garbage line\n")
    assert identity.mount_pools(table) == {os.makedev(0, 51): "tank", os.makedev(0, 64): "backup",
                                           os.makedev(8, 1): "/dev/sda1"}
    assert identity.mount_pools(os.path.join(tmp_path, "missing")) == {}
