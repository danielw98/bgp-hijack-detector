"""exabgp receive-process: read JSON updates from stdin and run each through the
detection pipeline. exabgp launches this and feeds it one JSON object per line.
"""

from __future__ import annotations

import json
import os
import sys

from .config import load_config
from .exabgp_parse import announcements_from
from .pipeline import Pipeline


def main() -> int:
    config_path = os.environ.get("BGPWATCH_CONFIG", "/opt/bgpwatch/config.toml")
    pipeline = Pipeline(load_config(config_path))

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
            pipeline.process(ann)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
