"""The replay loads an incident once, in batches, under its source name, and replaces
it only when forced. Runs against a fake connection: no Postgres and no MRT file."""

from __future__ import annotations

import os
import sys
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
os.environ.setdefault("BGPWATCH_SCHEMA", str(ROOT / "schema.sql"))

from bgpwatch.config import Config  # noqa: E402
from bgpwatch.models import Announcement, MonitoredPrefix  # noqa: E402
from bgpwatch.pipeline import BATCH_SIZE  # noqa: E402
from bgpwatch.replay import run  # noqa: E402

CFG = Config(
    database_url="",
    monitored=[MonitoredPrefix("208.65.152.0/22", {36561}, "YouTube")],
    vrps=[],
)
SEEN = datetime(2008, 2, 24, 18, 47, 57, tzinfo=timezone.utc)
SOURCE = "youtube-2008"


class FakeCursor:
    def __init__(self, batches: list, row=None) -> None:
        self._batches = batches
        self._row = row

    def fetchone(self):
        return self._row

    def executemany(self, sql: str, rows) -> None:
        self._batches.append((sql, list(rows)))

    def __enter__(self):
        return self

    def __exit__(self, *exc) -> None:
        return None


class FakeConnection:
    """Records every statement; answers the source count with the configured pair."""

    def __init__(self, counts: tuple[int, int] = (0, 0)) -> None:
        self.counts = counts
        self.statements: list[tuple[str, object]] = []
        self.batches: list[tuple[str, list]] = []

    def execute(self, sql: str, params=None) -> FakeCursor:
        self.statements.append((sql, params))
        return FakeCursor(self.batches, self.counts if "count(*)" in sql else None)

    def cursor(self) -> FakeCursor:
        return FakeCursor(self.batches)

    @contextmanager
    def transaction(self):
        yield

    def deletes(self) -> list[str]:
        return [sql.split(" WHERE")[0] for sql, _ in self.statements if sql.startswith("DELETE")]

    def rows_into(self, table: str) -> list[list]:
        return [rows for sql, rows in self.batches if f"INTO {table}" in sql]


def announcements(n: int) -> list[Announcement]:
    legit = Announcement("208.65.152.0/22", 36561, (3333, 36561), "193.0.0.56", seen_at=SEEN)
    hijack = Announcement("208.65.153.0/24", 17557, (3333, 3491, 17557), "193.0.0.56", seen_at=SEEN)
    return [hijack if i == 0 else legit for i in range(n)]


def test_run_skips_a_loaded_source_unless_forced() -> None:
    conn = FakeConnection(counts=(3258, 27))
    summary = run(CFG, SOURCE, [Path("x.gz")], force=False, conn=conn, read=lambda p: announcements(3))
    assert summary == {"source": SOURCE, "updates": 3258, "alerts": 27, "skipped": True}
    assert conn.batches == []
    assert conn.deletes() == []


def test_run_replaces_a_loaded_source_when_forced() -> None:
    conn = FakeConnection(counts=(3258, 27))
    summary = run(CFG, SOURCE, [Path("x.gz")], force=True, conn=conn, read=lambda p: announcements(3))
    assert conn.deletes() == ["DELETE FROM alerts", "DELETE FROM route_updates"]
    assert summary == {"source": SOURCE, "updates": 3, "alerts": 1, "skipped": False}


def test_run_loads_an_empty_source_in_batches_tagged_with_the_source() -> None:
    conn = FakeConnection()
    files = [Path("a.gz"), Path("b.gz")]
    summary = run(
        CFG, SOURCE, files, force=False, conn=conn, read=lambda p: announcements(BATCH_SIZE + 5)
    )
    assert [len(b) for b in conn.rows_into("route_updates")] == [BATCH_SIZE, 5, BATCH_SIZE, 5]
    assert [len(b) for b in conn.rows_into("alerts")] == [1, 1]
    tagged = {row[-1] for b in conn.rows_into("route_updates") + conn.rows_into("alerts") for row in b}
    assert tagged == {SOURCE}
    assert summary == {"source": SOURCE, "updates": 2 * (BATCH_SIZE + 5), "alerts": 2, "skipped": False}


def test_alert_rows_carry_the_covering_prefix_and_the_path() -> None:
    conn = FakeConnection()
    run(CFG, SOURCE, [Path("a.gz")], force=False, conn=conn, read=lambda p: announcements(1))
    (alert_row,) = conn.rows_into("alerts")[0]
    assert alert_row[1] == "subprefix_hijack"
    assert alert_row[2] == "208.65.153.0/24"
    assert alert_row[5] == "208.65.152.0/22"
    assert alert_row[7] == [3333, 3491, 17557]


if __name__ == "__main__":
    from _runner import run as run_tests

    run_tests(globals())
