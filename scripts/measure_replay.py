"""Measure a recorded incident without a database: what the detector raises on the
file, which collector peers carried the alerted routes, and how long it takes.
Prints JSON. Runs where bgpwatch and libmrt.so are, i.e. in the collector container:

Usage: docker exec -i app-collector python3 - <config.toml> <updates.mrt[.gz]> < scripts/measure_replay.py
"""

from __future__ import annotations

import json
import sys
import time
from collections import Counter

from bgpwatch.config import load_config
from bgpwatch.detect import HijackDetector
from bgpwatch.mrt import read_mrt


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        sys.stderr.write(__doc__)
        return 2
    cfg = load_config(argv[1])
    detector = HijackDetector(cfg.monitored, cfg.vrps)

    started = time.perf_counter()
    announcements = list(read_mrt(argv[2]))
    parsed = time.perf_counter()
    alerts = [alert for ann in announcements for alert in detector.inspect(ann)]
    detected = time.perf_counter()

    first_seen_by_peer: dict[str, str] = {}
    for alert in alerts:
        first_seen_by_peer.setdefault(alert.peer, alert.raised_at.isoformat())
    print(
        json.dumps(
            {
                "updates": len(announcements),
                "withdrawals": sum(ann.withdrawn for ann in announcements),
                "peers_in_file": len({ann.peer for ann in announcements}),
                "alerts": len(alerts),
                "alerts_by_type": Counter(alert.alert_type.value for alert in alerts),
                "alerted_prefixes": sorted({str(alert.prefix) for alert in alerts}),
                "alerted_origins": sorted({alert.observed_origin for alert in alerts}),
                "first_alert": alerts[0].raised_at.isoformat() if alerts else "",
                "last_alert": alerts[-1].raised_at.isoformat() if alerts else "",
                "first_alert_by_peer": first_seen_by_peer,
                "parse_seconds": round(parsed - started, 4),
                "detect_seconds": round(detected - parsed, 4),
            },
            indent=1,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
