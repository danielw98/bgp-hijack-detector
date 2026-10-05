"""Data types shared across the detector: observed announcements, the ground truth
it is checked against, and the alerts it raises."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from ipaddress import IPv4Network, IPv6Network, ip_network

IPNetwork = IPv4Network | IPv6Network


def _as_network(value: str | IPNetwork) -> IPNetwork:
    return value if isinstance(value, (IPv4Network, IPv6Network)) else ip_network(value)


def _now() -> datetime:
    return datetime.now(timezone.utc)


@dataclass(frozen=True)
class Announcement:
    """One prefix as seen in a BGP update from a collector peer."""

    prefix: IPNetwork
    origin_as: int | None
    as_path: tuple[int, ...] = ()
    peer: str = ""
    next_hop: str | None = None
    withdrawn: bool = False
    seen_at: datetime = field(default_factory=_now)

    def __post_init__(self) -> None:
        object.__setattr__(self, "prefix", _as_network(self.prefix))
        object.__setattr__(self, "as_path", tuple(self.as_path))


@dataclass(frozen=True)
class MonitoredPrefix:
    """A prefix whose legitimate origin AS(es) are known in advance."""

    prefix: IPNetwork
    expected_origins: frozenset[int]
    description: str = ""

    def __post_init__(self) -> None:
        object.__setattr__(self, "prefix", _as_network(self.prefix))
        object.__setattr__(self, "expected_origins", frozenset(self.expected_origins))


@dataclass(frozen=True)
class Roa:
    """A Validated ROA Payload: prefix, the longest length it authorizes, and origin."""

    prefix: IPNetwork
    max_length: int
    origin_as: int

    def __post_init__(self) -> None:
        object.__setattr__(self, "prefix", _as_network(self.prefix))


class RpkiState(str, Enum):
    VALID = "valid"
    INVALID = "invalid"
    NOT_FOUND = "not_found"


class AlertType(str, Enum):
    ORIGIN_HIJACK = "origin_hijack"
    SUBPREFIX_HIJACK = "subprefix_hijack"
    RPKI_INVALID = "rpki_invalid"


@dataclass(frozen=True)
class Alert:
    alert_type: AlertType
    prefix: IPNetwork
    observed_origin: int
    peer: str
    as_path: tuple[int, ...]
    detail: str
    expected_origins: frozenset[int] = frozenset()
    covering_prefix: IPNetwork | None = None
    raised_at: datetime = field(default_factory=_now)
