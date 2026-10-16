"""Record one real cycle of the lab for the tour: for every step, the lab snapshot after
its action, the rows the routers reported meanwhile (the route log), the alerts the
detector raised, and the kernel walks that prove the packet's fate. Writes
src/BGPWatch.Web/wwwroot/static/tour/cycle.json, which the tour replays with the lab down.

Runs against a local stack at rest; it resets the lab first and leaves it at rest.

Usage: python scripts/record_tour.py [--url http://localhost:8081/]
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "src" / "BGPWatch.Web" / "wwwroot" / "static" / "tour" / "cycle.json"
DESTINATIONS = ("203.0.113.1", "203.0.113.129")
SOURCES = ("attacker", "transit")
SETTLE_S = 4.0


class Api:
    def __init__(self, base: str) -> None:
        self.base = base.rstrip("/")

    def get(self, path: str):
        with urllib.request.urlopen(self.base + path, timeout=30) as response:
            return json.load(response)

    def post(self, path: str, body: dict | None = None):
        data = json.dumps(body or {}).encode()
        request = urllib.request.Request(self.base + path, data=data, headers={"Content-Type": "application/json"}, method="POST")
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)

    def last_id(self) -> int:
        rows = self.get("/api/lab/events?limit=1")
        return rows[-1]["id"] if rows else 0

    def rows_since(self, last: int) -> list:
        return self.get(f"/api/lab/events?since={last}&limit=500")

    def last_alert_id(self) -> int:
        alerts = self.get("/api/alerts?limit=1")
        return alerts[0]["id"] if alerts else 0

    def alerts_after(self, last: int) -> list:
        return sorted((a for a in self.get("/api/alerts?limit=200") if a["source"] == "lab" and a["id"] > last), key=lambda a: a["id"])

    def trace(self, sources: tuple[str, ...]) -> dict:
        """The kernel walks from each source router to each destination: trace[from][to]."""
        return {source: {to: self.post("/api/lab/trace", {"from": source, "to": to}) for to in DESTINATIONS} for source in sources}


def now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")


def at_rest(lab: dict) -> bool:
    attacker = next(r for r in lab["routers"] if r["role"] == "attacker")
    return not any(p["best"] and not p["as_path"] for p in attacker["paths"])


def step(api: Api, action, *, trace_from: tuple[str, ...] = (), at_least: int = 0) -> dict:
    """Runs an action and records what followed: the snapshot, the rows, the alerts, the traces.
    Fewer rows than at_least means the lab did not do what the step expects (someone else is
    using it, or validation was left on): the recording stops rather than lie."""
    before = api.get("/api/lab")
    since = api.last_id()
    alerts_before = api.last_alert_id()
    started = now()
    answer = action()
    # The app stamps the moment it issued the command, on the clock the alerts carry.
    started = (answer or {}).get("issued_at", started) if isinstance(answer, dict) else started
    time.sleep(SETTLE_S)
    record = {"started_at": started, "before": before, "after": api.get("/api/lab"), "rows": api.rows_since(since), "alerts": api.alerts_after(alerts_before)}
    if len(record["rows"]) < at_least:
        raise RuntimeError(f"expected at least {at_least} reported rows after the action, got {len(record['rows'])}")
    if trace_from:
        record["trace"] = api.trace(trace_from)
    return record


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="http://localhost:8081/")
    api = Api(parser.parse_args().url)

    api.post("/api/lab/rov", {"on": False})
    api.post("/api/lab/reset")
    time.sleep(SETTLE_S)
    if not at_rest(api.get("/api/lab")):
        print("the lab is not at rest after a reset; stopping", file=sys.stderr)
        return 1

    steps: dict[str, dict] = {}
    steps["1"] = {"action": "announce", **step(api, lambda: api.post("/api/lab/announce"), at_least=6)}
    steps["3"] = {"action": "packet", "before": steps["1"]["after"], "after": steps["1"]["after"], "rows": [], "alerts": [], "trace": api.trace(SOURCES)}
    steps["4"] = {"action": "trigger exact", **step(api, lambda: api.post("/api/lab/trigger", {"scenario": "exact"}), trace_from=SOURCES, at_least=1)}
    steps["5"] = {"action": "trigger subprefix", **step(api, lambda: api.post("/api/lab/trigger", {"scenario": "subprefix"}), trace_from=SOURCES, at_least=3)}
    steps["6"] = {"action": "detector", "before": steps["5"]["after"], "after": steps["5"]["after"], "rows": [], "alerts": steps["4"]["alerts"] + steps["5"]["alerts"]}
    steps["7"] = {"action": "reset", **step(api, lambda: api.post("/api/lab/reset"), at_least=4)}

    api.post("/api/lab/rov", {"on": True})
    time.sleep(SETTLE_S)
    steps["8"] = {"action": "defence", **step(api, lambda: api.post("/api/lab/trigger", {"scenario": "both"}), trace_from=SOURCES, at_least=2)}
    api.post("/api/lab/reset")
    api.post("/api/lab/rov", {"on": False})
    time.sleep(SETTLE_S)

    cycle = {"recorded_at": now(), "steps": steps}
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(cycle, indent=1), encoding="utf-8")
    counts = {n: (len(s["rows"]), len(s["alerts"])) for n, s in steps.items()}
    print(f"wrote {OUT.relative_to(ROOT)}: rows and alerts per step {counts}")
    return 0 if at_rest(api.get("/api/lab")) else 2


if __name__ == "__main__":
    sys.exit(main())
