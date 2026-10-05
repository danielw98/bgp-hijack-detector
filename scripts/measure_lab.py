"""Measure the lab scenario through the dashboard API: inject the hijack, time the
alerts as they reach the database, reset, repeat. Prints JSON. Every run appends its
updates and alerts to the demo database.

Usage: python scripts/measure_lab.py [runs] [dashboard-url]
"""

from __future__ import annotations

import json
import statistics
import sys
import time
import urllib.request
from collections import Counter

POLL_SECONDS = 0.05
QUIET_SECONDS = 4.0
TIMEOUT_SECONDS = 60.0
SETTLE_SECONDS = 8.0


def call(base: str, path: str, method: str = "GET"):
    request = urllib.request.Request(base + path, method=method)
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def newest_alert_id(base: str) -> int:
    alerts = call(base, "/api/alerts?limit=1")
    return alerts[0]["id"] if alerts else 0


def run_once(base: str) -> dict:
    """Trigger the hijack and collect the alerts raised until the table goes quiet."""
    baseline = newest_alert_id(base)
    started = time.perf_counter()
    call(base, "/api/lab/trigger", "POST")
    seen: dict[int, float] = {}
    alerts: dict[int, dict] = {}
    last_change = started
    while True:
        now = time.perf_counter()
        for alert in call(base, "/api/alerts?limit=100"):
            if alert["id"] > baseline and alert["id"] not in seen:
                seen[alert["id"]] = now - started
                alerts[alert["id"]] = alert
                last_change = now
        if seen and now - last_change > QUIET_SECONDS:
            break
        if now - started > TIMEOUT_SECONDS:
            break
        time.sleep(POLL_SECONDS)
    call(base, "/api/lab/reset", "POST")
    time.sleep(SETTLE_SECONDS)
    return {
        "alerts": len(seen),
        "first_alert_seconds": round(min(seen.values()), 3) if seen else None,
        "last_alert_seconds": round(max(seen.values()), 3) if seen else None,
        "by_type": Counter(alert["alert_type"] for alert in alerts.values()),
        "by_peer": Counter(alert["peer"] for alert in alerts.values()),
        "by_prefix": Counter(alert["prefix"] for alert in alerts.values()),
    }


def main(argv: list[str]) -> int:
    runs = int(argv[1]) if len(argv) > 1 else 5
    base = argv[2] if len(argv) > 2 else "http://localhost:8080"
    results = [run_once(base) for _ in range(runs)]
    firsts = [r["first_alert_seconds"] for r in results if r["first_alert_seconds"] is not None]
    lasts = [r["last_alert_seconds"] for r in results if r["last_alert_seconds"] is not None]
    summary = {
        "runs": runs,
        "first_alert_seconds_median": statistics.median(firsts) if firsts else None,
        "first_alert_seconds_max": max(firsts) if firsts else None,
        "last_alert_seconds_median": statistics.median(lasts) if lasts else None,
        "last_alert_seconds_max": max(lasts) if lasts else None,
    }
    print(json.dumps({"summary": summary, "runs": results}, indent=1))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
