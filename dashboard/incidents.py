"""Alerts grouped into incidents: one offending origin against one monitored prefix,
seen from one or more vantage points, open until the alerts pause for longer than
the gap. Pure functions over alert rows, so the grouping is unit-tested."""

from __future__ import annotations

from collections.abc import Iterable
from datetime import timedelta
from ipaddress import ip_network

GAP = timedelta(minutes=10)


def group(alerts: Iterable[dict], monitored: Iterable[str], gap: timedelta = GAP) -> list[dict]:
    """Incidents, newest first. ``monitored`` names the monitored prefixes, so the
    three alert types one announcement can raise land in the same incident."""
    networks = [ip_network(p) for p in monitored]
    incidents: list[dict] = []
    open_by_key: dict[tuple, dict] = {}
    for alert in sorted(alerts, key=lambda a: (a["raised_at"], a["id"])):
        key = (alert["source"], _monitored_of(alert, networks), alert["observed_origin"])
        incident = open_by_key.get(key)
        if incident is None or alert["raised_at"] - incident["last_seen"] > gap:
            incident = _start(key, alert)
            incidents.append(incident)
            open_by_key[key] = incident
        _extend(incident, alert)
    return sorted(incidents, key=lambda i: i["first_seen"], reverse=True)


def _monitored_of(alert: dict, networks: list) -> str:
    """The monitored prefix the alert belongs to: its covering prefix when it names one,
    else the longest monitored prefix containing it, else its own prefix."""
    if alert.get("covering_prefix"):
        return alert["covering_prefix"]
    prefix = ip_network(alert["prefix"])
    covering = [n for n in networks if n.version == prefix.version and prefix.subnet_of(n)]
    if not covering:
        return alert["prefix"]
    return str(max(covering, key=lambda n: n.prefixlen))


def _start(key: tuple, alert: dict) -> dict:
    source, prefix, origin = key
    return {
        "source": source,
        "prefix": prefix,
        "origin": origin,
        "expected_origins": [],
        "first_seen": alert["raised_at"],
        "last_seen": alert["raised_at"],
        "peers": [],
        "alert_types": [],
        "prefixes": [],
        "alert_ids": [],
    }


def _extend(incident: dict, alert: dict) -> None:
    incident["last_seen"] = max(incident["last_seen"], alert["raised_at"])
    incident["alert_ids"].append(alert["id"])
    _add(incident["peers"], alert["peer"])
    _add(incident["alert_types"], alert["alert_type"])
    _add(incident["prefixes"], alert["prefix"])
    for origin in alert.get("expected_origins") or []:
        _add(incident["expected_origins"], origin)


def _add(values: list, value) -> None:
    if value not in values:
        values.append(value)
        values.sort()
