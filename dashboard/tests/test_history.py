"""A prefix's history: the state per vantage point and the changes, from route rows."""

from __future__ import annotations

import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import history  # noqa: E402

T0 = datetime(2026, 10, 9, 18, 56, 0, tzinfo=timezone.utc)


def row(id: int, seconds: int, peer: str, prefix: str, origin, path: list[int], withdrawn=False) -> dict:
    return {
        "id": id,
        "seen_at": T0 + timedelta(seconds=seconds),
        "peer": peer,
        "prefix": prefix,
        "origin_as": origin,
        "as_path": path,
        "withdrawn": withdrawn,
    }


ROWS = [
    row(1, 0, "10.10.0.10", "203.0.113.0/24", 65001, [65001]),
    row(2, 0, "10.10.0.13", "203.0.113.0/24", 65001, [65000, 65001]),
    row(3, 60, "10.10.0.13", "203.0.113.0/24", 65003, [65003]),
    row(4, 60, "10.10.0.13", "203.0.113.0/25", 65003, [65003]),
    row(5, 60, "10.10.0.10", "203.0.113.0/24", 65001, [65001]),
    row(6, 120, "10.10.0.13", "203.0.113.0/25", None, [], withdrawn=True),
    row(7, 120, "10.10.0.13", "203.0.113.0/24", 65001, [65000, 65001]),
]


def test_current_holds_the_last_update_per_peer_and_prefix() -> None:
    state = history.current(reversed(ROWS))
    assert [(s["peer"], s["prefix"], s["origin_as"], s["withdrawn"]) for s in state] == [
        ("10.10.0.10", "203.0.113.0/24", 65001, False),
        ("10.10.0.13", "203.0.113.0/24", 65001, False),
        ("10.10.0.13", "203.0.113.0/25", None, True),
    ]


def test_changes_skip_updates_that_repeat_the_same_route() -> None:
    changed = history.changes(reversed(ROWS))
    assert [c["seen_at"] - T0 for c in changed] == [
        timedelta(seconds=120),
        timedelta(seconds=120),
        timedelta(seconds=60),
        timedelta(seconds=60),
        timedelta(seconds=0),
        timedelta(seconds=0),
    ]
    assert all(c["peer"] != "10.10.0.10" or c["seen_at"] == T0 for c in changed)


def test_a_change_carries_the_state_before_and_after() -> None:
    hijack = [c for c in history.changes(ROWS) if c["seen_at"] == T0 + timedelta(seconds=60) and c["prefix"] == "203.0.113.0/24"]
    assert hijack == [
        {
            "seen_at": T0 + timedelta(seconds=60),
            "peer": "10.10.0.13",
            "prefix": "203.0.113.0/24",
            "before": {"origin_as": 65001, "as_path": [65000, 65001], "withdrawn": False},
            "after": {"origin_as": 65003, "as_path": [65003], "withdrawn": False},
        }
    ]


def test_the_first_sighting_has_no_state_before() -> None:
    first = history.changes(ROWS)[-1]
    assert first["before"] is None
    assert first["after"]["origin_as"] == 65001
