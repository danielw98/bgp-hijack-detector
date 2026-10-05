"""Replay a recorded MRT updates file through the detection pipeline, as if the
collector were seeing it live. Prints a JSON summary on stdout.

Usage: python -m bgpwatch.replay <config.toml> <updates.mrt[.gz|.bz2]>
"""

from __future__ import annotations

import json
import sys

from .config import load_config
from .mrt import read_mrt
from .pipeline import Pipeline


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        sys.stderr.write(__doc__)
        return 2
    config_path, mrt_path = argv[1:]
    pipeline = Pipeline(load_config(config_path))
    pipeline.process_batch(read_mrt(mrt_path))
    print(json.dumps({"updates": pipeline.update_count, "alerts": pipeline.alert_count}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
