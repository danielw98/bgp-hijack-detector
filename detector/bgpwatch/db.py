"""Thin persistence layer over Postgres for route updates and alerts.

cidr/inet values are passed as strings and integer arrays as Python lists, so no
psycopg type adapters need registering. Every row names its source: 'lab' for the live
collector, the incident directory name for a replay.
"""

from __future__ import annotations

from collections.abc import Iterable
from pathlib import Path
from typing import TYPE_CHECKING

from .models import Alert, Announcement, MonitoredPrefix

if TYPE_CHECKING:
    import psycopg

UPDATE_SQL = """
    INSERT INTO route_updates
        (seen_at, peer, prefix, origin_as, as_path, next_hop, withdrawn, source)
    VALUES (%s, %s, %s, %s, %s, %s, %s, %s)
"""
ALERT_SQL = """
    INSERT INTO alerts
        (raised_at, alert_type, prefix, observed_origin, expected_origins,
         covering_prefix, peer, as_path, detail, source)
    VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
"""


def connect(dsn: str) -> psycopg.Connection:
    import psycopg

    return psycopg.connect(dsn, autocommit=True)


def ensure_schema(conn: psycopg.Connection, schema_path: str | Path) -> None:
    conn.execute(Path(schema_path).read_text(encoding="utf-8"))


def sync_monitored(conn: psycopg.Connection, monitored: Iterable[MonitoredPrefix]) -> None:
    for m in monitored:
        conn.execute(
            """
            INSERT INTO monitored_prefixes (prefix, expected_origins, description)
            VALUES (%s, %s, %s)
            ON CONFLICT (prefix) DO UPDATE
              SET expected_origins = EXCLUDED.expected_origins,
                  description = EXCLUDED.description
            """,
            (str(m.prefix), sorted(m.expected_origins), m.description),
        )


def record_update(conn: psycopg.Connection, ann: Announcement, source: str) -> None:
    conn.execute(UPDATE_SQL, _update_row(ann, source))


def record_updates(conn: psycopg.Connection, anns: Iterable[Announcement], source: str) -> None:
    _insert_many(conn, UPDATE_SQL, [_update_row(ann, source) for ann in anns])


def record_alert(conn: psycopg.Connection, alert: Alert, source: str) -> None:
    conn.execute(ALERT_SQL, _alert_row(alert, source))


def record_alerts(conn: psycopg.Connection, alerts: Iterable[Alert], source: str) -> None:
    _insert_many(conn, ALERT_SQL, [_alert_row(alert, source) for alert in alerts])


def _insert_many(conn: psycopg.Connection, sql: str, rows: list[tuple]) -> None:
    if not rows:
        return
    with conn.cursor() as cursor:
        cursor.executemany(sql, rows)


def source_counts(conn: psycopg.Connection, source: str) -> tuple[int, int]:
    """How many route updates and alerts the source has stored."""
    row = conn.execute(
        """
        SELECT (SELECT count(*) FROM route_updates WHERE source = %s),
               (SELECT count(*) FROM alerts WHERE source = %s)
        """,
        (source, source),
    ).fetchone()
    return int(row[0]), int(row[1])


def delete_source(conn: psycopg.Connection, source: str) -> None:
    conn.execute("DELETE FROM alerts WHERE source = %s", (source,))
    conn.execute("DELETE FROM route_updates WHERE source = %s", (source,))


def _update_row(ann: Announcement, source: str) -> tuple:
    return (
        ann.seen_at,
        ann.peer,
        str(ann.prefix),
        ann.origin_as,
        list(ann.as_path),
        ann.next_hop,
        ann.withdrawn,
        source,
    )


def _alert_row(alert: Alert, source: str) -> tuple:
    return (
        alert.raised_at,
        alert.alert_type.value,
        str(alert.prefix),
        alert.observed_origin,
        sorted(alert.expected_origins),
        str(alert.covering_prefix) if alert.covering_prefix else None,
        alert.peer,
        list(alert.as_path),
        alert.detail,
        source,
    )
