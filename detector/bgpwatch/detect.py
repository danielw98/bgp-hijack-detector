"""The detection core: classify each observed announcement against the known-good
origins and the RPKI payloads, and raise an alert when it does not match.

Pure and side-effect free so it can be unit-tested without a database or a live feed.
Prefix comparisons use the stdlib ``ipaddress`` module; for a large monitored set the
same interface can be backed by a radix trie (pytricia) without touching callers.
"""

from __future__ import annotations

from collections.abc import Iterable, Sequence

from .models import (
    Alert,
    AlertType,
    Announcement,
    IPNetwork,
    MonitoredPrefix,
    Roa,
    RpkiState,
)


def _covers(outer: IPNetwork, inner: IPNetwork) -> bool:
    """True when ``outer`` contains ``inner`` or is equal to it (same family)."""
    if outer.version != inner.version:
        return False
    return inner.subnet_of(outer)


def rpki_validity(announcement: Announcement, vrps: Sequence[Roa]) -> RpkiState:
    """RFC 6811 origin validation of one announcement against a set of VRPs."""
    covering = [v for v in vrps if _covers(v.prefix, announcement.prefix)]
    if not covering:
        return RpkiState.NOT_FOUND
    for vrp in covering:
        authorized_length = announcement.prefix.prefixlen <= vrp.max_length
        if authorized_length and announcement.origin_as == vrp.origin_as:
            return RpkiState.VALID
    return RpkiState.INVALID


class HijackDetector:
    """Checks announcements against the monitored prefixes and the RPKI payloads."""

    def __init__(
        self,
        monitored: Iterable[MonitoredPrefix],
        vrps: Iterable[Roa] = (),
    ) -> None:
        self._monitored = list(monitored)
        self._vrps = list(vrps)

    def inspect(self, announcement: Announcement) -> list[Alert]:
        """Return every alert the announcement warrants (possibly none, or several)."""
        if announcement.withdrawn:
            return []

        alerts: list[Alert] = []
        alerts.extend(self._origin_and_subprefix(announcement))
        alerts.extend(self._rpki(announcement))
        return alerts

    def _origin_and_subprefix(self, ann: Announcement) -> list[Alert]:
        alerts: list[Alert] = []
        for monitored in self._monitored:
            if monitored.prefix.version != ann.prefix.version:
                continue
            unexpected_origin = ann.origin_as not in monitored.expected_origins

            if ann.prefix == monitored.prefix and unexpected_origin:
                alerts.append(
                    Alert(
                        alert_type=AlertType.ORIGIN_HIJACK,
                        prefix=ann.prefix,
                        observed_origin=ann.origin_as,
                        peer=ann.peer,
                        as_path=ann.as_path,
                        raised_at=ann.seen_at,
                        expected_origins=monitored.expected_origins,
                        detail=(
                            f"{ann.prefix} announced by AS{ann.origin_as}, "
                            f"expected {_fmt_origins(monitored.expected_origins)}"
                        ),
                    )
                )
            elif (
                ann.prefix != monitored.prefix
                and _covers(monitored.prefix, ann.prefix)
                and unexpected_origin
            ):
                alerts.append(
                    Alert(
                        alert_type=AlertType.SUBPREFIX_HIJACK,
                        prefix=ann.prefix,
                        observed_origin=ann.origin_as,
                        peer=ann.peer,
                        as_path=ann.as_path,
                        raised_at=ann.seen_at,
                        expected_origins=monitored.expected_origins,
                        covering_prefix=monitored.prefix,
                        detail=(
                            f"more-specific {ann.prefix} of monitored {monitored.prefix} "
                            f"announced by AS{ann.origin_as}, "
                            f"expected {_fmt_origins(monitored.expected_origins)}"
                        ),
                    )
                )
        return alerts

    def _rpki(self, ann: Announcement) -> list[Alert]:
        if not self._vrps:
            return []
        if rpki_validity(ann, self._vrps) is not RpkiState.INVALID:
            return []
        return [
            Alert(
                alert_type=AlertType.RPKI_INVALID,
                prefix=ann.prefix,
                observed_origin=ann.origin_as,
                peer=ann.peer,
                as_path=ann.as_path,
                raised_at=ann.seen_at,
                detail=(
                    f"{ann.prefix} from AS{ann.origin_as} is RPKI-invalid "
                    "(no VRP authorizes this origin or length)"
                ),
            )
        ]


def _fmt_origins(origins: frozenset[int]) -> str:
    return ", ".join(f"AS{a}" for a in sorted(origins)) or "none"
