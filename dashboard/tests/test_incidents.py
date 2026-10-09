"""Alerts of one hijack become one incident; a pause longer than the gap opens a new one."""

from __future__ import annotations

import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import incidents  # noqa: E402

T0 = datetime(2026, 10, 9, 19, 2, 48, tzinfo=timezone.utc)
MONITORED = ["203.0.113.0/24", "208.65.152.0/22"]


def alert(id: int, at: datetime, alert_type: str, prefix: str, peer: str, covering=None, **extra) -> dict:
    return {
        "id": id,
        "raised_at": at,
        "alert_type": alert_type,
        "prefix": prefix,
        "observed_origin": 65003,
        "expected_origins": [65001] if alert_type != "rpki_invalid" else [],
        "covering_prefix": covering,
        "peer": peer,
        "source": "lab",
        **extra,
    }


LAB_HIJACK = [
    alert(1, T0, "origin_hijack", "203.0.113.0/24", "10.10.0.13"),
    alert(2, T0, "rpki_invalid", "203.0.113.0/24", "10.10.0.13"),
    alert(3, T0, "subprefix_hijack", "203.0.113.0/25", "10.10.0.13", covering="203.0.113.0/24"),
    alert(4, T0, "rpki_invalid", "203.0.113.0/25", "10.10.0.13"),
    alert(5, T0 + timedelta(milliseconds=90), "subprefix_hijack", "203.0.113.0/25", "10.10.0.10", covering="203.0.113.0/24"),
    alert(6, T0 + timedelta(milliseconds=90), "rpki_invalid", "203.0.113.0/25", "10.10.0.10"),
    alert(7, T0 + timedelta(milliseconds=110), "subprefix_hijack", "203.0.113.0/25", "10.10.0.11", covering="203.0.113.0/24"),
    alert(8, T0 + timedelta(milliseconds=110), "rpki_invalid", "203.0.113.0/25", "10.10.0.11"),
]


def test_the_eight_alerts_of_one_lab_hijack_form_one_incident() -> None:
    (incident,) = incidents.group(LAB_HIJACK, MONITORED)
    assert incident["prefix"] == "203.0.113.0/24"
    assert incident["origin"] == 65003
    assert incident["expected_origins"] == [65001]
    assert incident["peers"] == ["10.10.0.10", "10.10.0.11", "10.10.0.13"]
    assert incident["alert_types"] == ["origin_hijack", "rpki_invalid", "subprefix_hijack"]
    assert incident["prefixes"] == ["203.0.113.0/24", "203.0.113.0/25"]
    assert incident["alert_ids"] == list(range(1, 9))
    assert (incident["first_seen"], incident["last_seen"]) == (T0, T0 + timedelta(milliseconds=110))


def test_a_pause_longer_than_the_gap_starts_a_new_incident_newest_first() -> None:
    later = [dict(a, id=a["id"] + 10, raised_at=a["raised_at"] + timedelta(minutes=30)) for a in LAB_HIJACK]
    grouped = incidents.group(LAB_HIJACK + later, MONITORED)
    assert [i["first_seen"] for i in grouped] == [T0 + timedelta(minutes=30), T0]
    assert [len(i["alert_ids"]) for i in grouped] == [8, 8]


def test_alerts_within_the_gap_extend_the_open_incident() -> None:
    soon = [dict(a, id=a["id"] + 10, raised_at=a["raised_at"] + timedelta(minutes=9)) for a in LAB_HIJACK]
    (incident,) = incidents.group(LAB_HIJACK + soon, MONITORED)
    assert len(incident["alert_ids"]) == 16


def test_sources_and_origins_are_kept_apart() -> None:
    replay = [
        dict(alert(20, T0, "subprefix_hijack", "208.65.153.0/24", "193.0.0.56", covering="208.65.152.0/22"), source="youtube-2008", observed_origin=17557),
    ]
    other_origin = [dict(alert(30, T0, "origin_hijack", "203.0.113.0/24", "10.10.0.13"), observed_origin=65002)]
    grouped = incidents.group(LAB_HIJACK + replay + other_origin, MONITORED)
    assert sorted((i["source"], i["origin"]) for i in grouped) == [
        ("lab", 65002),
        ("lab", 65003),
        ("youtube-2008", 17557),
    ]


def test_an_alert_outside_every_monitored_prefix_keeps_its_own_prefix() -> None:
    stray = [alert(40, T0, "rpki_invalid", "198.51.100.0/24", "10.10.0.13")]
    (incident,) = incidents.group(stray, MONITORED)
    assert incident["prefix"] == "198.51.100.0/24"
