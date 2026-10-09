"""The history of a prefix as the route updates tell it: the current state at every
vantage point, and the moments when a vantage point's route changed. Pure functions
over route_updates rows in any order."""

from __future__ import annotations

from collections.abc import Iterable

STATE_FIELDS = ("origin_as", "as_path", "withdrawn")


def current(rows: Iterable[dict]) -> list[dict]:
    """The last update per (peer, prefix), ordered by peer then prefix."""
    last: dict[tuple[str, str], dict] = {}
    for row in _in_order(rows):
        last[(row["peer"], row["prefix"])] = row
    return [last[key] for key in sorted(last)]


def changes(rows: Iterable[dict]) -> list[dict]:
    """Every update that changed what a vantage point holds for a prefix, newest first,
    with the state before it (absent for the first sighting)."""
    seen: dict[tuple[str, str], dict] = {}
    out: list[dict] = []
    for row in _in_order(rows):
        key = (row["peer"], row["prefix"])
        before = seen.get(key)
        after = _state(row)
        if before is None or _state(before) != after:
            out.append(
                {
                    "seen_at": row["seen_at"],
                    "peer": row["peer"],
                    "prefix": row["prefix"],
                    "before": _state(before) if before else None,
                    "after": after,
                }
            )
        seen[key] = row
    out.reverse()
    return out


def _in_order(rows: Iterable[dict]) -> list[dict]:
    return sorted(rows, key=lambda r: (r["seen_at"], r["id"]))


def _state(row: dict) -> dict:
    return {field: row[field] for field in STATE_FIELDS}
