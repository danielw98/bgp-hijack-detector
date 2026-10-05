"""Read BGP updates from MRT files (RFC 6396), the format of the public RIPE RIS and
RouteViews archives, through the C parser in detector/mrt (built by its build.sh)."""

from __future__ import annotations

import bz2
import gzip
from collections.abc import Iterator
from datetime import datetime, timezone
from ipaddress import IPv4Network, IPv6Network, ip_address
from pathlib import Path

from cffi import FFI

from .models import Announcement, IPNetwork

_MRT_DIR = Path(__file__).resolve().parents[1] / "mrt"
_OPENERS = {".gz": gzip.open, ".bz2": bz2.open}


def _declarations(header: Path) -> str:
    """The header without its preprocessor lines, which cffi does not parse."""
    lines = header.read_text(encoding="ascii").splitlines()
    return "\n".join(line for line in lines if not line.startswith("#"))


_ffi = FFI()
_ffi.cdef(_declarations(_MRT_DIR / "mrt.h"))
_lib = _ffi.dlopen(str(_MRT_DIR / "build" / "libmrt.so"))


class MrtError(ValueError):
    """The MRT data holds a truncated or malformed record."""


def read_mrt(path: str | Path) -> Iterator[Announcement]:
    """Every announced or withdrawn prefix in an MRT updates file (plain, .gz or .bz2)."""
    path = Path(path)
    with _OPENERS.get(path.suffix, open)(path, "rb") as stream:
        data = stream.read()
    return parse_mrt(data)


def parse_mrt(data: bytes) -> Iterator[Announcement]:
    """Every announced or withdrawn prefix in MRT-encoded bytes."""
    buffer = _ffi.from_buffer("uint8_t[]", data)
    reader = _lib.mrt_open(buffer, len(data))
    if reader == _ffi.NULL:
        raise MemoryError("mrt_open")
    reader = _ffi.gc(reader, _lib.mrt_close)
    route = _ffi.new("mrt_route *")
    while (status := _lib.mrt_next(reader, route)) != _lib.MRT_END:
        if status == _lib.MRT_MALFORMED:
            raise MrtError("truncated or malformed MRT record")
        yield _announcement(route)


def _announcement(route) -> Announcement:
    as_path = tuple(_ffi.unpack(route.as_path, route.as_path_length))
    return Announcement(
        prefix=_network(route.prefix, route.prefix_length),
        origin_as=as_path[-1] if as_path else None,
        as_path=as_path,
        peer=_address(route.peer),
        next_hop=_address(route.next_hop) if route.next_hop.family else None,
        withdrawn=bool(route.withdrawn),
        seen_at=datetime.fromtimestamp(route.timestamp, timezone.utc),
    )


def _packed(address) -> bytes:
    return bytes(_ffi.buffer(address.bytes, 4 if address.family == 4 else 16))


def _address(address) -> str:
    return str(ip_address(_packed(address)))


def _network(address, prefix_length: int) -> IPNetwork:
    network = IPv4Network if address.family == 4 else IPv6Network
    return network((_packed(address), prefix_length))
