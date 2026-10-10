"""Cross-check the C MRT parser against mrtparse: for every file given, both must
yield the same routes (timestamp, peer, prefix, withdrawn, AS path, next hop).
Prints one JSON line per file and exits 1 on a difference.

Needs `pip install mrtparse` and the app image (docker compose build), whose dump verb
reads the file with the C parser.

Usage: python scripts/compare_mrtparse.py <updates.mrt[.gz|.bz2]>...
"""

from __future__ import annotations

import json
import sys
from collections import Counter
from datetime import datetime
from ipaddress import ip_address, ip_network
from pathlib import Path

from mrtparse import Reader

from dumped_routes import dumped_routes

BGP4MP_TYPES = {16, 17}
TWO_OCTET_SUBTYPES = {1, 6}
FOUR_OCTET_SUBTYPES = {4, 7}
UPDATE = 2
AS_PATH, NEXT_HOP, MP_REACH_NLRI, MP_UNREACH_NLRI, AS4_PATH = 2, 3, 14, 15, 17
AS_SET, AS_SEQUENCE = 1, 2
UNICAST = 1
IP_FAMILIES = {1, 2}

Route = tuple[int, str, str, bool, tuple[int, ...], str | None]


def code(field: dict) -> int:
    """mrtparse renders an enumerated field as {code: name}."""
    return int(next(iter(field)))


def networks(entries: list[dict]) -> list[str]:
    return [str(ip_network(f"{e['prefix']}/{e['length']}", strict=False)) for e in entries]


def canonical(address: str | None) -> str | None:
    """One spelling per address: mrtparse and ipaddress print IPv4-mapped IPv6 differently."""
    return str(ip_address(address)) if address else None


def segments(value: list[dict]) -> list[tuple[int, list[int]]]:
    return [(code(s["type"]), [int(a) for a in s["value"]]) for s in value]


def flat(path: list[tuple[int, list[int]]]) -> list[int]:
    return [asn for _kind, asns in path for asn in asns]


def hops(path: list[tuple[int, list[int]]]) -> int:
    """Path length as RFC 6793 counts it: an AS_SET is one hop."""
    return sum(1 if kind == AS_SET else len(asns) for kind, asns in path if kind in (AS_SET, AS_SEQUENCE))


def merged(path: list[tuple[int, list[int]]], as4_path: list[tuple[int, list[int]]]) -> list[int]:
    """RFC 6793 section 4.2.3: the leading hops of AS_PATH, then the whole AS4_PATH."""
    keep = hops(path) - hops(as4_path)
    if not as4_path or keep < 0:
        return flat(path)
    leading: list[int] = []
    for kind, asns in path:
        if keep <= 0:
            break
        taken = asns if kind == AS_SET else asns[:keep]
        leading += taken
        keep -= 1 if kind == AS_SET else len(taken)
    return leading + flat(as4_path)


def reference_routes(path: str) -> tuple[list[Route], Counter]:
    """The routes mrtparse reads from the record kinds the C parser handles."""
    routes: list[Route] = []
    skipped: Counter = Counter()
    for entry in Reader(path):
        data = entry.data
        subtype = code(data["subtype"])
        if code(data["type"]) not in BGP4MP_TYPES or subtype not in TWO_OCTET_SUBTYPES | FOUR_OCTET_SUBTYPES:
            skipped[next(iter(data["subtype"].values()))] += 1
            continue
        if "bgp_message" not in data:
            skipped["message mrtparse could not decode"] += 1
            continue
        message = data["bgp_message"]
        if code(message["type"]) != UPDATE:
            continue
        timestamp, peer = code(data["timestamp"]), data["peer_ip"]
        as_path: list[tuple[int, list[int]]] = []
        as4_path: list[tuple[int, list[int]]] = []
        next_hop = None
        multiprotocol: list[tuple[list[str], bool, str | None]] = []
        for attribute in message["path_attributes"]:
            kind, value = code(attribute["type"]), attribute["value"]
            if kind == AS_PATH:
                as_path = segments(value)
            elif kind == AS4_PATH:
                as4_path = segments(value)
            elif kind == NEXT_HOP:
                next_hop = value
            elif kind in (MP_REACH_NLRI, MP_UNREACH_NLRI):
                if code(value["afi"]) not in IP_FAMILIES or code(value["safi"]) != UNICAST:
                    continue
                if kind == MP_REACH_NLRI:
                    multiprotocol.append((networks(value["nlri"]), False, value["next_hop"][0]))
                else:
                    multiprotocol.append((networks(value["withdrawn_routes"]), True, None))
        path_asns = tuple(merged(as_path, as4_path) if subtype in TWO_OCTET_SUBTYPES else flat(as_path))
        runs = [(networks(message["withdrawn_routes"]), True, None)]
        runs += multiprotocol
        runs.append((networks(message["nlri"]), False, next_hop))
        for prefixes, withdrawn, hop in runs:
            for prefix in prefixes:
                routes.append((timestamp, peer, prefix, withdrawn, () if withdrawn else path_asns, canonical(hop)))
    return routes, skipped


def parser_routes(path: str) -> list[Route]:
    return [
        (int(datetime.fromisoformat(r["seen_at"]).timestamp()), r["peer"], r["prefix"], r["withdrawn"],
         tuple(r["as_path"]), canonical(r["next_hop"]))
        for r in dumped_routes(path)
    ]


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        sys.stderr.write(__doc__)
        return 2
    differences = 0
    for path in argv[1:]:
        expected, skipped = reference_routes(path)
        actual = parser_routes(path)
        only_reference = Counter(expected) - Counter(actual)
        only_parser = Counter(actual) - Counter(expected)
        differences += sum(only_reference.values()) + sum(only_parser.values())
        print(
            json.dumps(
                {
                    "file": Path(path).name,
                    "mrtparse_routes": len(expected),
                    "parser_routes": len(actual),
                    "only_mrtparse": sum(only_reference.values()),
                    "only_parser": sum(only_parser.values()),
                    "skipped_record_subtypes": skipped,
                    "sample_only_mrtparse": [list(r) for r in list(only_reference)[:3]],
                    "sample_only_parser": [list(r) for r in list(only_parser)[:3]],
                }
            )
        )
    return 1 if differences else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
