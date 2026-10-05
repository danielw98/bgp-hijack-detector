"""exabgp receive-process: read JSON updates from stdin, run the detector, and write
route history and alerts to Postgres. exabgp launches this and feeds it one JSON
object per line.
"""

from __future__ import annotations

import json
import os
import sys

from . import db
from .config import load_config
from .detect import HijackDetector
from .exabgp_parse import announcements_from


def _log(text: str) -> None:
    sys.stderr.write(f"[bgpwatch] {text}\n")
    sys.stderr.flush()


def _sync_monitored(conn, monitored) -> None:
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


def main() -> int:
    config_path = os.environ.get("BGPWATCH_CONFIG", "/opt/bgpwatch/config.toml")
    schema_path = os.environ.get("BGPWATCH_SCHEMA", "/opt/bgpwatch/schema.sql")

    cfg = load_config(config_path)
    detector = HijackDetector(cfg.monitored, cfg.vrps)
    conn = db.connect(cfg.database_url)
    db.ensure_schema(conn, schema_path)
    _sync_monitored(conn, cfg.monitored)
    _log(f"ready: {len(cfg.monitored)} monitored prefixes, {len(cfg.vrps)} VRPs")

    while True:
        line = sys.stdin.readline()
        if not line:
            break
        line = line.strip()
        if not line:
            continue
        try:
            message = json.loads(line)
        except json.JSONDecodeError:
            continue

        for ann in announcements_from(message):
            db.record_update(conn, ann)
            for alert in detector.inspect(ann):
                db.record_alert(conn, alert)
                _log(f"ALERT {alert.alert_type.value}: {alert.detail} (peer {alert.peer})")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
