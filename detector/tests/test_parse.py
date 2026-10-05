"""Parsing of exabgp JSON update messages. Pure stdlib."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from bgpwatch.exabgp_parse import announcements_from  # noqa: E402

ANNOUNCE = {
    "type": "update",
    "neighbor": {
        "address": {"local": "10.10.0.100", "peer": "10.10.0.13"},
        "asn": {"local": 65100, "peer": 65003},
        "message": {
            "update": {
                "attribute": {"origin": "igp", "as-path": [65000, 65003]},
                "announce": {
                    "ipv4 unicast": {
                        "10.10.0.13": [
                            {"nlri": "203.0.113.0/24"},
                            {"nlri": "203.0.113.0/25"},
                        ]
                    }
                },
            }
        },
    },
}

WITHDRAW = {
    "type": "update",
    "neighbor": {
        "address": {"local": "10.10.0.100", "peer": "10.10.0.13"},
        "message": {
            "update": {"withdraw": {"ipv4 unicast": [{"nlri": "203.0.113.0/24"}]}}
        },
    },
}

KEEPALIVE = {"type": "keepalive", "neighbor": {}}


def test_announce_yields_prefixes_with_origin_and_peer() -> None:
    anns = announcements_from(ANNOUNCE)
    assert len(anns) == 2
    prefixes = {str(a.prefix) for a in anns}
    assert prefixes == {"203.0.113.0/24", "203.0.113.0/25"}
    for a in anns:
        assert a.origin_as == 65003
        assert a.as_path == (65000, 65003)
        assert a.peer == "10.10.0.13"
        assert not a.withdrawn


def test_withdraw_is_marked() -> None:
    anns = announcements_from(WITHDRAW)
    assert len(anns) == 1
    assert anns[0].withdrawn
    assert str(anns[0].prefix) == "203.0.113.0/24"


def test_non_update_is_ignored() -> None:
    assert announcements_from(KEEPALIVE) == []


if __name__ == "__main__":
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_") and callable(v)]
    failed = 0
    for t in tests:
        try:
            t()
            print(f"PASS {t.__name__}")
        except AssertionError as exc:
            failed += 1
            print(f"FAIL {t.__name__}: {exc}")
    print(f"\n{len(tests) - failed}/{len(tests)} passed")
    sys.exit(1 if failed else 0)
