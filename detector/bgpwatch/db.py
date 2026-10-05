"""Thin persistence layer over Postgres for route updates and alerts.

cidr/inet values are passed as strings and integer arrays as Python lists, so no
psycopg type adapters need registering.
"""

from __future__ import annotations

from pathlib import Path

import psycopg

from .models import Alert, Announcement


def connect(dsn: str) -> psycopg.Connection:
    return psycopg.connect(dsn, autocommit=True)


def ensure_schema(conn: psycopg.Connection, schema_path: str | Path) -> None:
    conn.execute(Path(schema_path).read_text(encoding="utf-8"))


def record_update(conn: psycopg.Connection, ann: Announcement) -> None:
    conn.execute(
        """
        INSERT INTO route_updates (peer, prefix, origin_as, as_path, next_hop, withdrawn)
        VALUES (%s, %s, %s, %s, %s, %s)
        """,
        (
            ann.peer,
            str(ann.prefix),
            ann.origin_as,
            list(ann.as_path),
            ann.next_hop,
            ann.withdrawn,
        ),
    )


def record_alert(conn: psycopg.Connection, alert: Alert) -> None:
    conn.execute(
        """
        INSERT INTO alerts
            (alert_type, prefix, observed_origin, expected_origins,
             covering_prefix, peer, as_path, detail)
        VALUES (%s, %s, %s, %s, %s, %s, %s, %s)
        """,
        (
            alert.alert_type.value,
            str(alert.prefix),
            alert.observed_origin,
            sorted(alert.expected_origins),
            str(alert.covering_prefix) if alert.covering_prefix else None,
            alert.peer,
            list(alert.as_path),
            alert.detail,
        ),
    )
