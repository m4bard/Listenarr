"""A quarantine folder must never reach a folder already walked: a bind mount would defeat rule (a).

Seen through a bind mount, a library name looks like a quarantine name (outside the root by its path, on the
same device), and one quarantine folder mounted twice counts one name twice against ``nlink - 1``. Two
guards refuse the run (rc 2): a mount point at or under a quarantine folder, and a quarantine folder whose
(st_dev, st_ino) was already walked. The bind-mount tests need a private mount namespace: they run only under
``unshare -Urm`` with RELINK_USERNS=1 (the round-2 controls do this). Titles and ASINs are corpus values.
"""

from __future__ import annotations

import os
import subprocess
from pathlib import Path

import pytest
from test_size_placeholder import Library, left_out, library, relinked
from test_waivers import PRINCESS, princess_in_its_asin_folder

from listenarr_relink import identity, inventory
from listenarr_relink.paths import PathMap

__all__ = ["library"]  # the fixture, re-exported so pytest finds it here

in_namespace = pytest.mark.skipif(os.environ.get("RELINK_USERNS") != "1",
                                  reason="needs a private mount namespace: unshare -Urm with RELINK_USERNS=1")


def test_a_quarantine_folder_already_walked_under_a_root_refuses(tmp_path: Path) -> None:
    folder = tmp_path / "QUARANTINE-DUP" / "view"
    folder.mkdir(parents=True)
    library = inventory.Library([], {}, [])
    info = os.lstat(folder)
    library.walked_dirs.add((info.st_dev, info.st_ino))  # as if walk_root had reached it under the root
    with pytest.raises(inventory.QuarantineAliasError):
        inventory.walk_quarantine(str(tmp_path / "QUARANTINE-DUP"), library)


def test_a_quarantine_folder_reached_twice_refuses(tmp_path: Path) -> None:
    folder = tmp_path / "QUARANTINE-DUP"
    folder.mkdir()
    library = inventory.Library([], {}, [])
    inventory.walk_quarantine(str(folder), library)
    with pytest.raises(inventory.QuarantineAliasError):
        inventory.walk_quarantine(str(folder), library)


def test_walk_root_records_every_folder(tmp_path: Path) -> None:
    (tmp_path / "ROOT-A" / "sub").mkdir(parents=True)
    library = inventory.Library([str(tmp_path / "ROOT-A")], {}, [])
    inventory.walk_root(str(tmp_path / "ROOT-A"), PathMap(), library)
    expected = {(os.lstat(p).st_dev, os.lstat(p).st_ino) for p in (tmp_path / "ROOT-A", tmp_path / "ROOT-A" / "sub")}
    assert library.walked_dirs == expected


def test_mount_points_decode_the_table(tmp_path: Path) -> None:
    table = tmp_path / "mountinfo"
    table.write_text("36 25 0:51 / /ROOT-A rw - zfs tank/media rw\n"
                     "37 36 0:51 /books /QUARANTINE\\040DUP/view rw - zfs tank/media rw\n"
                     "short line\n", encoding="utf-8")
    assert identity.mount_points(str(table)) == ["/ROOT-A", "/QUARANTINE DUP/view"]
    assert identity.mount_points(str(tmp_path / "missing")) == []


def test_cli_refuses_a_quarantine_folder_holding_a_mount_point(library: Library,
                                                               monkeypatch: pytest.MonkeyPatch) -> None:
    princess_in_its_asin_folder(library)
    folder = library.top / "QUARANTINE-DUP"
    (folder / "view").mkdir(parents=True)
    rc, plan = library.plan("plan-plain", "--quarantine-dir", str(folder))
    assert rc == 0 and plan is not None and plan["quarantine_dirs"] == [str(folder)]
    monkeypatch.setattr(identity, "mount_points", lambda: ["/", str(folder / "view")])
    rc, plan = library.plan("plan-mount", "--quarantine-dir", str(folder))
    assert rc == 2 and plan is None


def bind(source: Path, target: Path) -> None:
    subprocess.run(["mount", "--bind", str(source), str(target)], check=True)


@pytest.mark.parametrize("guard", ["mount-point", "folder-identity"])
@in_namespace
def test_the_root_bind_mounted_inside_a_quarantine_folder_refuses(library: Library, guard: str,
                                                                  monkeypatch: pytest.MonkeyPatch) -> None:
    row, new = princess_in_its_asin_folder(library)
    elsewhere = library.top / "ELSEWHERE" / "seed.mp3"  # the real other name: not a quarantine name
    elsewhere.parent.mkdir()
    os.link(new, elsewhere)
    folder = library.top / "QUARANTINE-DUP"
    (folder / "view").mkdir(parents=True)
    _, control = library.plan("plan-control", "--quarantine-dir", str(folder))
    assert control is not None and relinked(control) == {} and "hard links" in left_out(control)[row]
    bind(library.root, folder / "view")
    if guard == "folder-identity":
        monkeypatch.setattr(identity, "mount_points", lambda: [])  # only the walked-folder guard is left
    rc, plan = library.plan("plan-bound", "--quarantine-dir", str(folder))
    assert rc == 2 and plan is None


@pytest.mark.parametrize("guard", ["mount-point", "folder-identity"])
@in_namespace
def test_a_quarantine_folder_bind_mounted_twice_refuses(library: Library, guard: str,
                                                        monkeypatch: pytest.MonkeyPatch) -> None:
    row, new = princess_in_its_asin_folder(library)
    folder = library.top / "QUARANTINE-DUP"
    held = folder / PRINCESS[1] / new.parent.name / new.name
    held.parent.mkdir(parents=True)
    os.link(new, held)
    elsewhere = library.top / "ELSEWHERE" / "seed.mp3"
    elsewhere.parent.mkdir()
    os.link(new, elsewhere)  # nlink 3: under the root, in the quarantine, and elsewhere
    (folder / "mirror").mkdir()
    _, control = library.plan("plan-control", "--quarantine-dir", str(folder))
    assert control is not None and relinked(control) == {} and "hard links" in left_out(control)[row]
    bind(folder / PRINCESS[1], folder / "mirror")
    if guard == "folder-identity":
        monkeypatch.setattr(identity, "mount_points", lambda: [])
    rc, plan = library.plan("plan-bound", "--quarantine-dir", str(folder))
    assert rc == 2 and plan is None


def test_each_up_front_quarantine_check_refuses_on_its_own(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    # The walked-folder guard would catch these too; each up-front check must hold without it.
    from listenarr_relink.cli import _quarantine_dirs
    monkeypatch.setattr(identity, "mount_points", lambda: [])
    root, outside = tmp_path / "ROOT-A", tmp_path / "QUARANTINE-DUP"
    (root / "inside").mkdir(parents=True)
    (outside / "sub").mkdir(parents=True)
    host_roots = [str(root)]
    assert _quarantine_dirs([str(outside)], host_roots) == [str(outside)]
    for values in ([str(root / "inside")], [str(tmp_path)], [str(outside), str(outside)],
                   [str(outside), str(outside / "sub")], [str(tmp_path / "missing")]):
        assert _quarantine_dirs(values, host_roots) is None, values
