"""The path every observed announcement takes: into route history, through the
detector, and its alerts into Postgres. Shared by the live collector and the replay."""

from __future__ import annotations

import os
import sys
from collections.abc import Iterable

from . import db
from .config import Config
from .detect import HijackDetector
from .models import Announcement

SCHEMA_PATH = os.environ.get("BGPWATCH_SCHEMA", "/opt/bgpwatch/schema.sql")


def log(text: str) -> None:
    sys.stderr.write(f"[bgpwatch] {text}\n")
    sys.stderr.flush()


class Pipeline:
    def __init__(self, cfg: Config) -> None:
        self.update_count = 0
        self.alert_count = 0
        self._detector = HijackDetector(cfg.monitored, cfg.vrps)
        self._conn = db.connect(cfg.database_url)
        db.ensure_schema(self._conn, SCHEMA_PATH)
        db.sync_monitored(self._conn, cfg.monitored)
        log(f"ready: {len(cfg.monitored)} monitored prefixes, {len(cfg.vrps)} VRPs")

    def process(self, ann: Announcement) -> None:
        db.record_update(self._conn, ann)
        self.update_count += 1
        for alert in self._detector.inspect(ann):
            db.record_alert(self._conn, alert)
            self.alert_count += 1
            log(f"ALERT {alert.alert_type.value}: {alert.detail} (peer {alert.peer})")

    def process_batch(self, announcements: Iterable[Announcement]) -> None:
        """Process many announcements in one transaction."""
        with self._conn.transaction():
            for ann in announcements:
                self.process(ann)
