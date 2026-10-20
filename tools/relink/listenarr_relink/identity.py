"""Physical object identity in Listenarr's persisted spelling, computed read-only.

Listenarr (.620) stores ``linux-generation:{major:x8}:{minor:x8}:{inode:x16}:fh:{type:x8}:{hex}``
(or ``...:gen:{x8}``; the older ``linux:...:{birth}:{ns}:fh|gen...`` spelling also exists). A match
on the strong generation key means the candidate IS the object the row was registered with, even
after a rename. A bare inode match is not identity: inodes are reused after delete.
"""

from __future__ import annotations

import ctypes
import ctypes.util
import errno
import fcntl
import os
import struct
from dataclasses import dataclass

_AT_EMPTY_PATH = 0x1000
_AT_HANDLE_FID = 0x0200
_MAX_HANDLE_BYTES = 128
_FS_IOC_GETVERSION = 0x80087601

_libc = ctypes.CDLL(ctypes.util.find_library("c") or "libc.so.6", use_errno=True)
_name_to_handle_at = _libc.name_to_handle_at
_name_to_handle_at.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_void_p,
                               ctypes.POINTER(ctypes.c_int), ctypes.c_int]
_name_to_handle_at.restype = ctypes.c_int


@dataclass(frozen=True)
class StoredIdentity:
    """A parsed persisted identity: device, inode and the strong generation suffixes."""

    major: int
    minor: int
    inode: int
    generations: frozenset[str]


@dataclass(frozen=True)
class LiveIdentity:
    """Identity evidence read from an open file."""

    major: int
    minor: int
    inode: int
    generations: frozenset[str]

    def matches(self, stored: StoredIdentity) -> bool:
        """Same device, inode and at least one strong generation suffix."""
        return ((self.major, self.minor, self.inode) == (stored.major, stored.minor, stored.inode)
                and bool(self.generations & stored.generations))


def _is_hex(text: str, width: int | None = None) -> bool:
    if not text or (width is not None and len(text) != width):
        return False
    try:
        int(text, 16)
    except ValueError:
        return False
    return True


def _generation_suffix(parts: list[str]) -> str | None:
    if len(parts) == 2 and parts[0] == "gen" and _is_hex(parts[1], 8):
        return f"gen:{parts[1]}"
    if len(parts) == 3 and parts[0] == "fh" and _is_hex(parts[1], 8) and _is_hex(parts[2]) and len(parts[2]) % 2 == 0:
        return f"fh:{parts[1]}:{parts[2].lower()}"
    return None


def parse_stored(identity: str | None) -> StoredIdentity | None:
    """Parse either persisted Linux spelling; None for anything else (including NULL)."""
    if not identity:
        return None
    parts = identity.split(":")
    if parts[0] == "linux-generation" and len(parts) >= 6 and all(_is_hex(p, w) for p, w in
                                                                    zip(parts[1:4], (8, 8, 16), strict=True)):
        suffix = _generation_suffix(parts[4:])
        offset = 1
    elif parts[0] == "linux" and len(parts) >= 8 and all(_is_hex(p, w) for p, w in
                                                         zip(parts[1:6], (8, 8, 16, 16, 8), strict=True)):
        suffix = _generation_suffix(parts[6:])
        offset = 1
    else:
        return None
    if suffix is None:
        return None
    return StoredIdentity(int(parts[offset], 16), int(parts[offset + 1], 16), int(parts[offset + 2], 16),
                          frozenset({suffix}))


def _file_handle(fd: int) -> str | None:
    for flags in (_AT_EMPTY_PATH | _AT_HANDLE_FID, _AT_EMPTY_PATH):
        buffer = ctypes.create_string_buffer(8 + _MAX_HANDLE_BYTES)
        struct.pack_into("Ii", buffer, 0, _MAX_HANDLE_BYTES, 0)
        mount_id = ctypes.c_int(0)
        if _name_to_handle_at(fd, b"", buffer, ctypes.byref(mount_id), flags) == 0:
            size, handle_type = struct.unpack_from("Ii", buffer, 0)
            return f"fh:{handle_type:08x}:{buffer.raw[8:8 + size].hex()}"
        if ctypes.get_errno() != errno.EINVAL:
            return None
    return None


def _inode_generation(fd: int) -> str | None:
    try:
        raw = fcntl.ioctl(fd, _FS_IOC_GETVERSION, b"\0" * 8)
    except OSError:
        return None
    return f"gen:{struct.unpack('<I', raw[:4])[0]:08x}"


def read_live(path: str) -> LiveIdentity | None:
    """Open ``path`` read-only without following a final symlink and read its identity evidence."""
    flags = os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC
    try:
        # O_NOATIME keeps even the access time unchanged where the kernel allows it (file owner).
        fd = os.open(path, flags | os.O_NOATIME)
    except PermissionError:
        try:
            fd = os.open(path, flags)
        except OSError:
            return None
    except OSError:
        return None
    try:
        info = os.fstat(fd)
        generations = {g for g in (_file_handle(fd), _inode_generation(fd)) if g}
        return LiveIdentity(os.major(info.st_dev), os.minor(info.st_dev), info.st_ino, frozenset(generations))
    finally:
        os.close(fd)
