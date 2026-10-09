"""The path every observed announcement takes: into route history, through the
detector, and its alerts into Postgres. Shared by the live collector and the replay."""

from __future__ import annotations

import os
import sys
from collections.abc import Iterable, Iterator
from itertools import islice

from . import db
from .config import Config
from .detect import HijackDetector
from .models import Alert, Announcement

SCHEMA_PATH = os.environ.get("BGPWATCH_SCHEMA", "/opt/bgpwatch/schema.sql")
BATCH_SIZE = 1000


def log(text: str) -> None:
    sys.stderr.write(f"[bgpwatch] {text}\n")
    sys.stderr.flush()


class Pipeline:
    def __init__(self, cfg: Config, source: str = "lab", conn=None) -> None:
        self.source = source
        self.update_count = 0
        self.alert_count = 0
        self._detector = HijackDetector(cfg.monitored, cfg.vrps)
        self._conn = conn if conn is not None else db.connect(cfg.database_url)
        db.ensure_schema(self._conn, SCHEMA_PATH)
        db.sync_monitored(self._conn, cfg.monitored)
        log(f"ready: {len(cfg.monitored)} monitored prefixes, {len(cfg.vrps)} VRPs")

    def process(self, ann: Announcement) -> None:
        db.record_update(self._conn, ann, self.source)
        self.update_count += 1
        for alert in self._detector.inspect(ann):
            db.record_alert(self._conn, alert, self.source)
            self.alert_count += 1
            _log_alert(alert)

    def process_batch(self, announcements: Iterable[Announcement]) -> None:
        """Process many announcements in one transaction, written in batches."""
        with self._conn.transaction():
            for chunk in _chunks(announcements, BATCH_SIZE):
                alerts = [alert for ann in chunk for alert in self._detector.inspect(ann)]
                db.record_updates(self._conn, chunk, self.source)
                db.record_alerts(self._conn, alerts, self.source)
                self.update_count += len(chunk)
                self.alert_count += len(alerts)
                for alert in alerts:
                    _log_alert(alert)


def _log_alert(alert: Alert) -> None:
    log(f"ALERT {alert.alert_type.value}: {alert.detail} (peer {alert.peer})")


def _chunks(items: Iterable[Announcement], size: int) -> Iterator[list[Announcement]]:
    iterator = iter(items)
    while chunk := list(islice(iterator, size)):
        yield chunk
