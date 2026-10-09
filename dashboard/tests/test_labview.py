"""Lab-view tests against vtysh output captured from the FRR 10.4.5 lab during the
hijack (next-hop fields captured on 2026-10-09)."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import labview  # noqa: E402

SUMMARY = """{
  "routerId":"10.10.0.10",
  "as":65000,
  "peers":{
    "10.10.0.11":{"hostname":"victim","remoteAs":65001,"state":"Established","peerState":"OK"},
    "10.10.0.13":{"hostname":"attacker","remoteAs":65003,"state":"Active","peerState":"OK"},
    "10.10.0.100":{"remoteAs":65100,"state":"Established","peerState":"OK"}
  },
  "totalPeers":3
}"""

TRANSIT_TABLE = """{
 "vrfId": 0,
 "routerId": "10.10.0.10",
 "localAS": 65000,
 "routes": { "203.0.113.0/24": [{"valid":true,"pathFrom":"external","peerId":"10.10.0.13","path":"65003","origin":"IGP","nexthops":[{"ip":"10.10.0.13","hostname":"attacker","afi":"ipv4","used":true}]},{"valid":true,"bestpath":true,"selectionReason":"Older Path","pathFrom":"external","peerId":"10.10.0.11","path":"65001","origin":"IGP","nexthops":[{"ip":"10.10.0.11","hostname":"victim","afi":"ipv4","used":true}]}]
,"203.0.113.0/25": [{"valid":true,"bestpath":true,"selectionReason":"First path received","pathFrom":"external","peerId":"10.10.0.13","path":"65003","origin":"IGP","nexthops":[{"ip":"10.10.0.13","hostname":"attacker","afi":"ipv4","used":true}]}]
 } , "totalRoutes": 2, "totalPaths": 3 } """

VICTIM_TABLE = """{
 "localAS": 65001,
 "routes": { "203.0.113.0/24": [{"valid":true,"bestpath":true,"selectionReason":"First path received","weight":32768,"peerId":"(unspec)","path":"","origin":"IGP","nexthops":[{"ip":"0.0.0.0","hostname":"victim","afi":"ipv4","used":true}]}]
,"203.0.113.0/25": [{"valid":true,"bestpath":true,"selectionReason":"First path received","peerId":"10.10.0.10","path":"65000 65003","origin":"IGP","nexthops":[{"ip":"10.10.0.10","hostname":"transit","afi":"ipv4","used":true}]}]
 } , "totalRoutes": 2, "totalPaths": 2 } """


def _table(text: str) -> dict:
    return labview.split_documents(SUMMARY + "\n" + text)[1]


def test_split_documents_returns_summary_then_table() -> None:
    summary, table = labview.split_documents(SUMMARY + "\n" + TRANSIT_TABLE)
    assert summary["as"] == 65000
    assert table["localAS"] == 65000


def test_split_documents_rejects_output_without_json() -> None:
    try:
        labview.split_documents("bgpd is not running\n")
    except ValueError:
        return
    raise AssertionError("expected ValueError")


def test_sessions_report_each_neighbor_and_whether_it_is_established() -> None:
    summary, _ = labview.split_documents(SUMMARY + "\n" + TRANSIT_TABLE)
    assert labview.sessions(summary) == [
        {"peer": "10.10.0.11", "remote_as": 65001, "established": True},
        {"peer": "10.10.0.13", "remote_as": 65003, "established": False},
        {"peer": "10.10.0.100", "remote_as": 65100, "established": True},
    ]


def test_paths_put_the_chosen_path_of_a_prefix_first() -> None:
    exact = [p for p in labview.paths(_table(TRANSIT_TABLE)) if p["prefix"] == "203.0.113.0/24"]
    assert [(p["origin_as"], p["best"]) for p in exact] == [(65001, True), (65003, False)]
    assert exact[0]["reason"] == "Older Path"


def test_paths_keep_the_losing_path_without_a_reason() -> None:
    spare = labview.paths(_table(TRANSIT_TABLE))[1]
    assert spare == {
        "prefix": "203.0.113.0/24",
        "as_path": [65003],
        "origin_as": 65003,
        "next_hop": "10.10.0.13",
        "best": False,
        "reason": "",
    }


def test_local_path_has_an_empty_as_path_the_router_as_origin_and_no_next_hop() -> None:
    local = labview.paths(_table(VICTIM_TABLE))[0]
    assert local["as_path"] == []
    assert local["origin_as"] == 65001
    assert local["next_hop"] is None


def test_learned_path_takes_its_origin_from_the_last_as_and_its_next_hop_from_the_neighbor() -> None:
    learned = labview.paths(_table(VICTIM_TABLE))[1]
    assert learned["as_path"] == [65000, 65003]
    assert learned["origin_as"] == 65003
    assert learned["next_hop"] == "10.10.0.10"


def test_table_without_routes_has_no_paths() -> None:
    assert labview.paths({}) == []


def test_snapshot_reads_every_router_with_the_show_command() -> None:
    asked = []

    def run(container: str, command: list[str]) -> str:
        asked.append((container, command))
        return SUMMARY + "\n" + VICTIM_TABLE

    states = labview.snapshot(run)
    assert [s["role"] for s in states] == ["victim", "transit", "attacker"]
    assert all(s["reachable"] for s in states)
    assert asked == [(r.container, labview.SHOW_COMMAND) for r in labview.ROUTERS]


def test_snapshot_names_the_addresses_a_router_answers_for() -> None:
    def run(container: str, command: list[str]) -> str:
        return SUMMARY + "\n" + VICTIM_TABLE

    victim, transit, _ = labview.snapshot(run)
    assert victim["hosts"] == ["203.0.113.1", "203.0.113.129"]
    assert transit["hosts"] == []


def test_snapshot_draws_a_failing_router_as_unreachable() -> None:
    def run(container: str, command: list[str]) -> str:
        raise RuntimeError("container is not running")

    victim = labview.snapshot(run)[0]
    assert victim == {
        "role": "victim",
        "address": "10.10.0.11",
        "asn": 65001,
        "hosts": ["203.0.113.1", "203.0.113.129"],
        "reachable": False,
        "sessions": [],
        "paths": [],
    }
