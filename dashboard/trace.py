"""Where a real packet would go in the lab, asked of each router's kernel with
``ip route get``: the forwarding verdict of one router, and the walk from router to
router that follows the verdicts until the packet is delivered or dropped.

No packet is sent. Docker drops any bridged packet addressed outside an internal
network's subnet (rule DOCKER-INTERNAL ... ! -d 10.10.0.0/24 -j DROP), so a ping to
203.0.113.1 can never cross the lab, while the kernel's own forwarding decision is
exactly what a packet would follow.
"""

from __future__ import annotations

import re
from collections.abc import Callable

ROUTE_GET = ["ip", "route", "get"]
MAX_HOPS = 6
VIA = re.compile(r"\bvia (\S+) dev (\S+)")
DIRECT = re.compile(r"^(?:local )?\S+ dev (\S+)")
OUTCOME = {
    "local": "delivered",
    "direct": "delivered",
    "blackhole": "dropped",
    "unreachable": "unreachable",
    "unknown": "unknown",
}


def verdict(stdout: str, stderr: str) -> dict:
    """The kernel's answer to ``ip route get``: forwarded via a neighbor, delivered
    locally, sent directly on a link, dropped by a blackhole route, or unreachable."""
    text = stdout.strip()
    if match := VIA.search(text):
        return {"kind": "via", "next_hop": match.group(1), "dev": match.group(2)}
    if match := DIRECT.match(text):
        kind = "local" if text.startswith("local ") else "direct"
        return {"kind": kind, "next_hop": None, "dev": match.group(1)}
    if "Invalid argument" in stderr:
        return {"kind": "blackhole", "next_hop": None, "dev": None}
    if "unreachable" in stderr.lower():
        return {"kind": "unreachable", "next_hop": None, "dev": None}
    return {"kind": "unknown", "next_hop": None, "dev": None}


def walk(
    start: str,
    destination: str,
    routers: dict[str, str],
    ask: Callable[[str, str], dict],
) -> dict:
    """Follow the verdicts from router ``start`` toward ``destination``. ``routers``
    maps each router's link address to its name; ``ask(router, destination)`` returns
    that router's verdict. The outcome is delivered, dropped, unreachable, lost (the
    next hop is no lab router), unknown, or loop."""
    hops: list[dict] = []
    router = start
    for _ in range(MAX_HOPS):
        answer = ask(router, destination)
        hops.append({"router": router, **answer})
        if answer["kind"] != "via":
            return {"hops": hops, "outcome": OUTCOME[answer["kind"]]}
        router = routers.get(answer["next_hop"], "")
        if not router:
            return {"hops": hops, "outcome": "lost"}
    return {"hops": hops, "outcome": "loop"}
