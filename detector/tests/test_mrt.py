"""The C MRT parser behind bgpwatch.mrt, on hand-built records and on the recorded
2008 incident. Needs the library built first: sh detector/mrt/build.sh."""

from __future__ import annotations

import struct
import sys
from datetime import datetime, timezone
from ipaddress import ip_address, ip_network
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from bgpwatch.config import load_config  # noqa: E402
from bgpwatch.detect import HijackDetector  # noqa: E402
from bgpwatch.models import AlertType  # noqa: E402
from bgpwatch.mrt import MrtError, parse_mrt, read_mrt  # noqa: E402

INCIDENT = Path(__file__).resolve().parents[1] / "incidents" / "youtube-2008"
TIMESTAMP = 1203878877
PEER = "192.0.2.1"
BGP4MP, BGP4MP_ET = 16, 17
STATE_CHANGE, MESSAGE, MESSAGE_AS4 = 0, 1, 4
UPDATE, KEEPALIVE = 2, 4
AS_SET, AS_SEQUENCE = 1, 2
AS_TRANS = 23456


def _prefix(text: str) -> bytes:
    network = ip_network(text)
    used = (network.prefixlen + 7) // 8
    return bytes([network.prefixlen]) + network.network_address.packed[:used]


def _attribute(code: int, value: bytes) -> bytes:
    return bytes([0x40, code, len(value)]) + value


def _as_path(asns: list[int], width: int = 2, segment: int = AS_SEQUENCE) -> bytes:
    return bytes([segment, len(asns)]) + b"".join(a.to_bytes(width, "big") for a in asns)


def _bgp(body: bytes, message_type: int = UPDATE) -> bytes:
    return b"\xff" * 16 + struct.pack(">HB", 19 + len(body), message_type) + body


def _update(withdrawn: bytes = b"", attributes: bytes = b"", nlri: bytes = b"") -> bytes:
    sizes = struct.pack(">H", len(withdrawn)), struct.pack(">H", len(attributes))
    return _bgp(sizes[0] + withdrawn + sizes[1] + attributes + nlri)


def _record(message: bytes, subtype: int = MESSAGE, mrt_type: int = BGP4MP) -> bytes:
    width = 4 if subtype == MESSAGE_AS4 else 2
    peer = ip_address(PEER).packed
    body = (
        (b"\x00\x00\x12\x34" if mrt_type == BGP4MP_ET else b"")
        + (64500).to_bytes(width, "big")
        + (64496).to_bytes(width, "big")
        + struct.pack(">HH", 0, 1)
        + peer
        + bytes(len(peer))
        + message
    )
    return struct.pack(">IHHI", TIMESTAMP, mrt_type, subtype, len(body)) + body


ANNOUNCE = _update(
    attributes=_attribute(2, _as_path([64500, 64501])) + _attribute(3, ip_address(PEER).packed),
    nlri=_prefix("203.0.113.0/24") + _prefix("198.51.100.0/25"),
)


def _raises_mrt_error(data: bytes) -> bool:
    try:
        list(parse_mrt(data))
    except MrtError:
        return True
    return False


def test_announced_prefixes_share_path_peer_and_next_hop() -> None:
    anns = list(parse_mrt(_record(ANNOUNCE)))
    assert [str(a.prefix) for a in anns] == ["203.0.113.0/24", "198.51.100.0/25"]
    for a in anns:
        assert a.as_path == (64500, 64501)
        assert a.origin_as == 64501
        assert a.peer == PEER
        assert a.next_hop == PEER
        assert not a.withdrawn


def test_record_timestamp_becomes_seen_at() -> None:
    (first, _second) = parse_mrt(_record(ANNOUNCE))
    assert first.seen_at == datetime.fromtimestamp(TIMESTAMP, timezone.utc)


def test_withdrawn_prefix_carries_no_path() -> None:
    (ann,) = parse_mrt(_record(_update(withdrawn=_prefix("203.0.113.0/24"))))
    assert ann.withdrawn
    assert str(ann.prefix) == "203.0.113.0/24"
    assert ann.as_path == ()
    assert ann.origin_as is None
    assert ann.next_hop is None


def test_withdrawals_come_before_announcements_of_the_same_update() -> None:
    message = _update(
        withdrawn=_prefix("198.51.100.0/24"),
        attributes=_attribute(2, _as_path([64500])),
        nlri=_prefix("203.0.113.0/24"),
    )
    assert [a.withdrawn for a in parse_mrt(_record(message))] == [True, False]


def test_four_octet_session_reads_four_octet_as_numbers() -> None:
    message = _update(
        attributes=_attribute(2, _as_path([64500, 65550], width=4)),
        nlri=_prefix("203.0.113.0/24"),
    )
    (ann,) = parse_mrt(_record(message, subtype=MESSAGE_AS4))
    assert ann.as_path == (64500, 65550)


def test_as4_path_replaces_as_trans_on_a_two_octet_session() -> None:
    message = _update(
        attributes=_attribute(2, _as_path([64500, AS_TRANS]))
        + _attribute(17, _as_path([65550], width=4)),
        nlri=_prefix("203.0.113.0/24"),
    )
    (ann,) = parse_mrt(_record(message))
    assert ann.as_path == (64500, 65550)


def test_as_set_members_are_flattened_after_the_sequence() -> None:
    path = _as_path([64500]) + _as_path([64501, 64502], segment=AS_SET)
    message = _update(attributes=_attribute(2, path), nlri=_prefix("203.0.113.0/24"))
    (ann,) = parse_mrt(_record(message))
    assert ann.as_path == (64500, 64501, 64502)


def test_extended_length_attribute() -> None:
    path = _as_path([64500, 64501])
    extended = bytes([0x50, 2]) + struct.pack(">H", len(path)) + path
    (ann,) = parse_mrt(_record(_update(attributes=extended, nlri=_prefix("203.0.113.0/24"))))
    assert ann.as_path == (64500, 64501)


def test_ipv6_prefixes_come_from_the_multiprotocol_attributes() -> None:
    next_hop = ip_address("2001:db8::1")
    reach = struct.pack(">HBB", 2, 1, 16) + next_hop.packed + b"\x00" + _prefix("2001:db8:1::/48")
    unreach = struct.pack(">HB", 2, 1) + _prefix("2001:db8:2::/48")
    attributes = (
        _attribute(15, unreach) + _attribute(2, _as_path([64500])) + _attribute(14, reach)
    )
    withdrawn, announced = parse_mrt(_record(_update(attributes=attributes)))
    assert (str(withdrawn.prefix), withdrawn.withdrawn) == ("2001:db8:2::/48", True)
    assert (str(announced.prefix), announced.withdrawn) == ("2001:db8:1::/48", False)
    assert announced.next_hop == str(next_hop)
    assert announced.as_path == (64500,)


def test_bits_past_the_prefix_length_are_cleared() -> None:
    sloppy = bytes([23, 203, 0, 113])
    (ann,) = parse_mrt(_record(_update(attributes=_attribute(2, _as_path([64500])), nlri=sloppy)))
    assert str(ann.prefix) == "203.0.112.0/23"


def test_microsecond_record_type() -> None:
    (first, _second) = parse_mrt(_record(ANNOUNCE, mrt_type=BGP4MP_ET))
    assert str(first.prefix) == "203.0.113.0/24"


def test_records_without_an_update_yield_nothing() -> None:
    state_change = struct.pack(">IHHI", TIMESTAMP, BGP4MP, STATE_CHANGE, 4) + bytes(4)
    keepalive = _record(_bgp(b"", message_type=KEEPALIVE))
    assert list(parse_mrt(state_change + keepalive)) == []


def test_records_are_read_in_sequence() -> None:
    assert len(list(parse_mrt(_record(ANNOUNCE) * 3))) == 6


def test_empty_input_yields_nothing() -> None:
    assert list(parse_mrt(b"")) == []


def test_truncated_record_is_an_error() -> None:
    assert _raises_mrt_error(_record(ANNOUNCE)[:-1])


def test_prefix_longer_than_its_family_is_an_error() -> None:
    too_long = bytes([33, 203, 0, 113, 0, 0])
    assert _raises_mrt_error(_record(_update(nlri=too_long)))


def test_as_path_running_past_its_attribute_is_an_error() -> None:
    short = bytes([AS_SEQUENCE, 3]) + (64500).to_bytes(2, "big")
    message = _update(attributes=_attribute(2, short), nlri=_prefix("203.0.113.0/24"))
    assert _raises_mrt_error(_record(message))


def test_incident_file_shows_the_hijack_at_the_documented_minute() -> None:
    hijacked = ip_network("208.65.153.0/24")
    first = next(a for a in read_mrt(INCIDENT / "updates.20080224.1845.gz") if a.prefix == hijacked)
    assert first.seen_at.strftime("%Y-%m-%d %H:%M") == "2008-02-24 18:47"
    assert first.as_path[-2:] == (3491, 17557)


def test_incident_replay_raises_only_the_subprefix_hijack() -> None:
    cfg = load_config(INCIDENT / "config.toml")
    detector = HijackDetector(cfg.monitored, cfg.vrps)
    alerts = [
        alert
        for ann in read_mrt(INCIDENT / "updates.20080224.1845.gz")
        for alert in detector.inspect(ann)
    ]
    assert len(alerts) == 27
    for alert in alerts:
        assert alert.alert_type is AlertType.SUBPREFIX_HIJACK
        assert str(alert.prefix) == "208.65.153.0/24"
        assert alert.observed_origin == 17557
        assert str(alert.covering_prefix) == "208.65.152.0/22"


if __name__ == "__main__":
    from _runner import run

    run(globals())
