"""A drifted device minor end to end: PLAN, explain-identity and APPLY on a throwaway library of real files.

The library's files are real (ffmpeg); only the stored device minor is shifted by one, as a ZFS dataset's
anonymous device minor shifts when it is mounted again. Titles and authors are corpus values.
"""

from __future__ import annotations

import json
import sqlite3

import pytest
from test_size_placeholder import PLACEHOLDER, PRINCESS, Library, library, relinked

from listenarr_relink import cli

__all__ = ["library"]  # the fixture, re-exported so pytest finds it here


def drift(library: Library, row: int) -> str:
    """Shift the row's stored device minor by one; return the new stored identity."""
    with sqlite3.connect(library.db) as connection:
        (stored,) = connection.execute("SELECT PhysicalObjectIdentity FROM AudiobookFiles WHERE Id = ?",
                                       (row,)).fetchone()
    parts = str(stored).split(":")
    parts[2] = f"{(int(parts[2], 16) + 1) % 0x100000:08x}"
    drifted = ":".join(parts)
    library.set_row(row, PhysicalObjectIdentity=drifted)
    return drifted


def test_drifted_minor_relinks_and_applies(library: Library, capsys: pytest.CaptureFixture[str]) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER)
    drift(library, row)
    new = library.move(row)
    rc, plan = library.plan("plan")
    assert rc == 0 and plan is not None
    assert relinked(plan) == {row: str(new)}
    entry = plan["relinks"][0]
    assert entry["confidence"] == "identity" and entry["evidence"]["identity_device_drift"] is True
    assert entry["keep_physical_identity"] is False  # the stored identity no longer spells the device
    capsys.readouterr()
    assert cli.main(["explain-identity", "--db", str(library.db), "--workers", "1"]) == 0
    out = capsys.readouterr().out.splitlines()
    assert "  matched 1" in out and "matched_device_drift 1" in out
    assert any(line.startswith("identity_matched_before_vetoes 1 ") for line in out)
    assert not any(str(library.root) in line or "Princess" in line for line in out)
    assert library.apply("plan") == 0
    assert library.paths() == {row: str(new)}
    with sqlite3.connect(library.db) as connection:
        assert connection.execute("SELECT PhysicalObjectIdentity FROM AudiobookFiles").fetchone() == (None,)


def test_explain_identity_writes_rows_only_where_asked(library: Library, capsys: pytest.CaptureFixture[str]) -> None:
    (row,) = library.add_book(1, PRINCESS, 1, Size=PLACEHOLDER)
    drift(library, row)
    library.move(row)
    db = str(library.db)
    inside = library.root / "rows.tsv"
    assert cli.main(["explain-identity", "--db", db, "--rows-out", str(inside)]) == 2
    assert cli.main(["explain-identity", "--db", db, "--with-paths"]) == 2
    assert cli.main(["explain-identity", "--db", db, "--rows-out", str(library.top / "missing" / "rows.tsv")]) == 2
    rows = library.top / "rows.tsv"
    assert cli.main(["explain-identity", "--db", db, "--rows-out", str(rows)]) == 0
    assert rows.read_text().splitlines()[1].split("\t") == [str(row), "1", "matched", "Y", "unknown"]
    assert cli.main(["explain-identity", "--db", db, "--rows-out", str(rows)]) == 2  # never overwrites
    assert not inside.exists()


def test_verify_reports_a_live_row_on_a_drifted_minor_apart(library: Library,
                                                            capsys: pytest.CaptureFixture[str]) -> None:
    drifted, exact = library.add_book(1, PRINCESS, 2, Size=PLACEHOLDER)
    drift(library, drifted)  # both files stay at their stored paths: both rows are live
    capsys.readouterr()
    assert cli.main(["verify", "--db", str(library.db)]) == 0
    status = json.loads(capsys.readouterr().out)["status"]
    assert status.get("live-identity-device-drift") == 1 and status.get("live-identity-match") == 1
    assert "live-identity-mismatch" not in status
    assert exact != drifted
