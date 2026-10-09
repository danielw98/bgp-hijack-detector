"""Replay recorded MRT updates files through the detection pipeline, as if the
collector were seeing them live, and print a JSON summary on stdout.

Usage: python -m bgpwatch.replay <config.toml> [<updates.mrt[.gz|.bz2]> ...] [--force]

Without file arguments every ``updates.*`` file beside the config is replayed, in name
order. The rows are stored under the incident's source name, the config's directory
name. A source already in the database is left alone, unless --force replaces it.
"""

from __future__ import annotations

import argparse
import json
import sys
from collections.abc import Callable, Iterable
from pathlib import Path

from . import db
from .config import Config, load_config
from .models import Announcement
from .mrt import read_mrt
from .pipeline import Pipeline


def main(argv: list[str]) -> int:
    args = _parse(argv[1:])
    config_path = Path(args.config)
    cfg = load_config(config_path)
    files = [Path(p) for p in args.mrt] or sorted(config_path.parent.glob("updates.*"))
    summary = run(
        cfg,
        config_path.resolve().parent.name,
        files,
        force=args.force,
        conn=db.connect(cfg.database_url),
    )
    print(json.dumps(summary))
    return 0


def run(
    cfg: Config,
    source: str,
    files: Iterable[Path],
    *,
    force: bool,
    conn,
    read: Callable[[Path], Iterable[Announcement]] = read_mrt,
) -> dict:
    """Load the files under ``source`` unless it is already loaded; report the counts."""
    pipeline = Pipeline(cfg, source=source, conn=conn)
    updates, alerts = db.source_counts(conn, source)
    if updates and not force:
        return {"source": source, "updates": updates, "alerts": alerts, "skipped": True}
    if updates:
        db.delete_source(conn, source)
    for path in files:
        pipeline.process_batch(read(path))
    return {
        "source": source,
        "updates": pipeline.update_count,
        "alerts": pipeline.alert_count,
        "skipped": False,
    }


def _parse(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="bgpwatch.replay", description=__doc__)
    parser.add_argument("config", help="incident config.toml (monitored prefixes, VRPs)")
    parser.add_argument(
        "mrt", nargs="*", help="MRT updates files; default: every updates.* beside the config"
    )
    parser.add_argument(
        "--force", action="store_true", help="replace the source when it is already loaded"
    )
    return parser.parse_args(argv)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
