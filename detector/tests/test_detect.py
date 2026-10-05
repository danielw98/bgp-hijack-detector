"""Detection-core tests. Pure stdlib, so they run with either pytest or plain python."""

from __future__ import annotations

import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from bgpwatch.detect import HijackDetector, rpki_validity  # noqa: E402
from bgpwatch.models import (  # noqa: E402
    AlertType,
    Announcement,
    MonitoredPrefix,
    Roa,
    RpkiState,
)

MONITORED = [MonitoredPrefix("203.0.113.0/24", {65001}, "lab victim")]
VRPS = [Roa("203.0.113.0/24", max_length=24, origin_as=65001)]


def _types(alerts) -> set[AlertType]:
    return {a.alert_type for a in alerts}


def test_alert_carries_the_time_the_announcement_was_seen() -> None:
    seen_at = datetime(2008, 2, 24, 18, 47, tzinfo=timezone.utc)
    hijack = Announcement("203.0.113.0/24", 65003, (65003,), seen_at=seen_at)
    (alert,) = HijackDetector(MONITORED).inspect(hijack)
    assert alert.raised_at == seen_at


def test_legitimate_announcement_is_silent() -> None:
    det = HijackDetector(MONITORED, VRPS)
    alerts = det.inspect(Announcement("203.0.113.0/24", 65001, (65001,), peer="transit"))
    assert alerts == []


def test_exact_prefix_origin_hijack() -> None:
    det = HijackDetector(MONITORED, VRPS)
    alerts = det.inspect(Announcement("203.0.113.0/24", 65003, (65003,), peer="transit"))
    assert AlertType.ORIGIN_HIJACK in _types(alerts)
    assert AlertType.RPKI_INVALID in _types(alerts)


def test_more_specific_subprefix_hijack() -> None:
    det = HijackDetector(MONITORED, VRPS)
    alerts = det.inspect(Announcement("203.0.113.0/25", 65003, (65003,), peer="transit"))
    assert AlertType.SUBPREFIX_HIJACK in _types(alerts)
    assert AlertType.RPKI_INVALID in _types(alerts)


def test_more_specific_from_legit_origin_is_rpki_invalid_only() -> None:
    # Same (correct) origin, but the length exceeds the ROA maxLength -> RPKI-invalid,
    # yet no origin/sub-prefix hijack because the origin is expected.
    det = HijackDetector(MONITORED, VRPS)
    alerts = det.inspect(Announcement("203.0.113.0/25", 65001, (65001,), peer="transit"))
    assert _types(alerts) == {AlertType.RPKI_INVALID}


def test_withdrawn_update_is_ignored() -> None:
    det = HijackDetector(MONITORED, VRPS)
    alerts = det.inspect(Announcement("203.0.113.0/24", 65003, withdrawn=True))
    assert alerts == []


def test_unrelated_prefix_is_silent() -> None:
    det = HijackDetector(MONITORED, VRPS)
    alerts = det.inspect(Announcement("198.51.100.0/24", 65099, (65099,), peer="transit"))
    assert alerts == []


def test_rpki_states() -> None:
    assert rpki_validity(Announcement("203.0.113.0/24", 65001), VRPS) is RpkiState.VALID
    assert rpki_validity(Announcement("203.0.113.0/24", 65003), VRPS) is RpkiState.INVALID
    assert rpki_validity(Announcement("203.0.113.0/25", 65001), VRPS) is RpkiState.INVALID
    assert rpki_validity(Announcement("198.51.100.0/24", 65099), VRPS) is RpkiState.NOT_FOUND


if __name__ == "__main__":
    from _runner import run

    run(globals())
