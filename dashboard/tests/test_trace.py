"""Kernel forwarding verdicts (iproute2 6.15 output captured in the lab on 2026-10-09)
and the router-to-router walk built on them."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import trace  # noqa: E402

VIA = "203.0.113.1 via 10.10.0.11 dev eth0 src 10.10.0.10 uid 0 \n    cache \n"
LOCAL = "local 203.0.113.1 dev lo src 203.0.113.1 uid 0 \n    cache <local> \n"
DIRECT = "10.10.0.11 dev eth0 src 10.10.0.10 uid 0 \n    cache \n"
BLACKHOLE = "RTNETLINK answers: Invalid argument\n"
UNREACHABLE = "RTNETLINK answers: Network unreachable\n"

ROUTERS = {"10.10.0.11": "victim", "10.10.0.10": "transit", "10.10.0.13": "attacker"}


def test_verdict_reads_the_next_hop_of_a_forwarded_packet() -> None:
    assert trace.verdict(VIA, "") == {"kind": "via", "next_hop": "10.10.0.11", "dev": "eth0"}


def test_verdict_tells_local_delivery_from_a_direct_link() -> None:
    assert trace.verdict(LOCAL, "") == {"kind": "local", "next_hop": None, "dev": "lo"}
    assert trace.verdict(DIRECT, "") == {"kind": "direct", "next_hop": None, "dev": "eth0"}


def test_verdict_reads_blackhole_and_unreachable_from_the_error() -> None:
    assert trace.verdict("", BLACKHOLE)["kind"] == "blackhole"
    assert trace.verdict("", UNREACHABLE)["kind"] == "unreachable"
    assert trace.verdict("", "")["kind"] == "unknown"


def answers(table: dict) -> tuple[list, object]:
    asked = []

    def ask(router: str, destination: str) -> dict:
        asked.append((router, destination))
        return table[router]

    return asked, ask


def test_walk_at_baseline_is_delivered_at_the_victim() -> None:
    asked, ask = answers({"transit": trace.verdict(VIA, ""), "victim": trace.verdict(LOCAL, "")})
    result = trace.walk("transit", "203.0.113.1", ROUTERS, ask)
    assert result["outcome"] == "delivered"
    assert [(h["router"], h["kind"]) for h in result["hops"]] == [("transit", "via"), ("victim", "local")]
    assert asked == [("transit", "203.0.113.1"), ("victim", "203.0.113.1")]


def test_walk_during_the_hijack_is_dropped_at_the_attacker() -> None:
    to_attacker = VIA.replace("10.10.0.11", "10.10.0.13")
    _, ask = answers({"transit": trace.verdict(to_attacker, ""), "attacker": trace.verdict("", BLACKHOLE)})
    result = trace.walk("transit", "203.0.113.1", ROUTERS, ask)
    assert result["outcome"] == "dropped"
    assert [h["router"] for h in result["hops"]] == ["transit", "attacker"]


def test_walk_stops_when_the_next_hop_is_no_lab_router() -> None:
    _, ask = answers({"transit": trace.verdict(VIA.replace("10.10.0.11", "10.10.0.99"), "")})
    assert trace.walk("transit", "203.0.113.1", ROUTERS, ask)["outcome"] == "lost"


def test_walk_gives_up_on_a_loop() -> None:
    to_transit = VIA.replace("10.10.0.11", "10.10.0.10")
    _, ask = answers({"transit": trace.verdict(VIA, ""), "victim": trace.verdict(to_transit, "")})
    result = trace.walk("transit", "203.0.113.1", ROUTERS, ask)
    assert result["outcome"] == "loop"
    assert len(result["hops"]) == trace.MAX_HOPS
