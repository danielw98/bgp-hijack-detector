"""Run the MRT parser's sanitizer fuzz (detector/mrt/fuzz.sh) in a container that has
gcc: the parser's build stage of the app image (Dockerfile, stage mrt), with no network. Exits with the
harness's exit code.

Usage: python scripts/fuzz_mrt.py [iterations]
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
IMAGE = "bgp-hijack-mrt-build"
SEED = "incidents/youtube-2008/updates.20080224.1845.gz"


def main(argv: list[str]) -> int:
    iterations = argv[1] if len(argv) > 1 else "20000"
    subprocess.run(
        ["docker", "build", "--quiet", "--target", "mrt", "-t", IMAGE, "."],
        cwd=ROOT,
        check=True,
    )
    fuzz = subprocess.run(
        [
            "docker", "run", "--rm", "--network", "none",
            "-v", f"{ROOT / 'detector'}:/detector:ro",
            IMAGE, "sh", "/detector/mrt/fuzz.sh", f"/detector/{SEED}", iterations,
        ]
    )
    return fuzz.returncode


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
