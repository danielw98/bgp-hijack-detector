"""The lab as its routers report it: the BGP sessions and the BGP table of each FRR
router, read with vtysh and reduced to what the page draws.

ROUTERS mirrors lab/docker-compose.yml and lab/frr/*/frr.conf; COLLECTOR mirrors
detector/exabgp.conf.
"""

from __future__ import annotations

import json
from collections.abc import Callable
from dataclasses import dataclass

SHOW_COMMAND = [
    "vtysh",
    "-c",
    "show bgp ipv4 unicast summary json",
    "-c",
    "show ip bgp json",
]
COLLECTOR = {"address": "10.10.0.100", "asn": 65100}


@dataclass(frozen=True)
class Router:
    role: str
    container: str
    address: str
    asn: int


ROUTERS = (
    Router("victim", "lab-victim", "10.10.0.11", 65001),
    Router("transit", "lab-transit", "10.10.0.10", 65000),
    Router("attacker", "lab-attacker", "10.10.0.13", 65003),
)


def snapshot(run: Callable[[str, list[str]], str]) -> list[dict]:
    """The state of every lab router. ``run`` executes a command in a container and
    returns its stdout."""
    return [_router_state(router, run) for router in ROUTERS]


def _router_state(router: Router, run: Callable[[str, list[str]], str]) -> dict:
    state = {"role": router.role, "address": router.address, "asn": router.asn}
    try:
        summary, table = split_documents(run(router.container, SHOW_COMMAND))
    except Exception:  # a stopped or still booting router is drawn as unreachable
        return {**state, "reachable": False, "sessions": [], "paths": []}
    return {**state, "reachable": True, "sessions": sessions(summary), "paths": paths(table)}


def split_documents(stdout: str) -> tuple[dict, dict]:
    """The two JSON documents SHOW_COMMAND prints back to back: summary, then table."""
    decoder = json.JSONDecoder()
    summary, end = decoder.raw_decode(stdout, stdout.index("{"))
    table, _ = decoder.raw_decode(stdout, stdout.index("{", end))
    return summary, table


def sessions(summary: dict) -> list[dict]:
    """One entry per configured BGP neighbor of the router."""
    return [
        {
            "peer": address,
            "remote_as": peer["remoteAs"],
            "established": peer["state"] == "Established",
        }
        for address, peer in summary["peers"].items()
    ]


def paths(table: dict) -> list[dict]:
    """Every path in the router's BGP table, the chosen path of each prefix first."""
    rows = []
    for prefix, entries in table.get("routes", {}).items():
        for entry in sorted(entries, key=_is_spare):
            as_path = [int(asn) for asn in entry["path"].split()]
            rows.append(
                {
                    "prefix": prefix,
                    "as_path": as_path,
                    "origin_as": as_path[-1] if as_path else table["localAS"],
                    "best": not _is_spare(entry),
                    "reason": entry.get("selectionReason", ""),
                }
            )
    return rows


def _is_spare(entry: dict) -> bool:
    return not entry.get("bestpath", False)
