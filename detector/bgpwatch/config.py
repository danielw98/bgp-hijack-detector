"""Load the detector's ground truth (monitored prefixes, RPKI payloads) and its
database URL from a TOML file. The database URL may be overridden by the
BGPWATCH_DATABASE_URL environment variable."""

from __future__ import annotations

import os
import tomllib
from dataclasses import dataclass
from pathlib import Path

from .models import MonitoredPrefix, Roa


@dataclass(frozen=True)
class Config:
    database_url: str
    monitored: list[MonitoredPrefix]
    vrps: list[Roa]


def load_config(path: str | Path) -> Config:
    data = tomllib.loads(Path(path).read_text(encoding="utf-8"))

    monitored = [
        MonitoredPrefix(
            prefix=entry["prefix"],
            expected_origins=entry["expected_origins"],
            description=entry.get("description", ""),
        )
        for entry in data.get("monitored", [])
    ]
    vrps = [
        Roa(
            prefix=entry["prefix"],
            max_length=entry["max_length"],
            origin_as=entry["origin_as"],
        )
        for entry in data.get("vrp", [])
    ]
    database_url = os.environ.get("BGPWATCH_DATABASE_URL", data.get("database_url", ""))
    return Config(database_url=database_url, monitored=monitored, vrps=vrps)
