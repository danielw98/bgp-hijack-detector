"""Turn one exabgp JSON update message into Announcement objects.

Kept free of any database or exabgp dependency so it can be unit-tested on its own.
"""

from __future__ import annotations

from collections.abc import Iterator

from .models import Announcement


def _flatten_as_path(as_path: object) -> list[int]:
    """Flatten an exabgp as-path (which may nest lists for AS_SET) into plain ints."""
    out: list[int] = []
    if isinstance(as_path, (list, tuple)):
        for element in as_path:
            if isinstance(element, int):
                out.append(element)
            elif isinstance(element, (list, tuple)):
                out.extend(x for x in element if isinstance(x, int))
    return out


def announcements_from(message: dict) -> list[Announcement]:
    """Every prefix (announced or withdrawn) carried by one exabgp update message."""
    if message.get("type") != "update":
        return []

    neighbor = message.get("neighbor", {})
    peer = neighbor.get("address", {}).get("peer", "")
    update = neighbor.get("message", {}).get("update", {})

    as_path = tuple(_flatten_as_path(update.get("attribute", {}).get("as-path", [])))
    origin_as = as_path[-1] if as_path else None

    results: list[Announcement] = []

    for _afi_safi, nexthop_map in update.get("announce", {}).items():
        if not isinstance(nexthop_map, dict):
            continue
        for nexthop, prefixes in nexthop_map.items():
            for prefix in _as_prefixes(prefixes):
                results.append(
                    Announcement(
                        prefix=prefix,
                        origin_as=origin_as,
                        as_path=as_path,
                        peer=peer,
                        next_hop=None if nexthop == "null" else nexthop,
                    )
                )

    for _afi_safi, prefixes in update.get("withdraw", {}).items():
        for prefix in _as_prefixes(prefixes):
            results.append(
                Announcement(
                    prefix=prefix,
                    origin_as=None,
                    as_path=(),
                    peer=peer,
                    withdrawn=True,
                )
            )

    return results


def _as_prefixes(prefixes: object) -> Iterator[str]:
    """exabgp carries prefixes as a list of ``{"nlri": "<prefix>"}`` objects; tolerate
    a plain string list and the dict-keyed form from other versions."""
    if isinstance(prefixes, dict):
        yield from prefixes.keys()
    elif isinstance(prefixes, (list, tuple)):
        for item in prefixes:
            if isinstance(item, str):
                yield item
            elif isinstance(item, dict) and "nlri" in item:
                yield item["nlri"]
