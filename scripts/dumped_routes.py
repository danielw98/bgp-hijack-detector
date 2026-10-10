"""The routes of an MRT updates file as the app's C parser reads them: runs the app
image's dump verb (docker compose build first) on the file, one JSON object per route
with seen_at, peer, prefix, withdrawn, as_path and next_hop."""

from __future__ import annotations

import json
import subprocess
from collections.abc import Iterator
from pathlib import Path

IMAGE = "bgp-hijack-app"


def dumped_routes(path: str | Path) -> Iterator[dict]:
    path = Path(path).resolve()
    command = [
        "docker", "run", "--rm", "--network", "none",
        "-v", f"{path.parent}:/in:ro", IMAGE, "dump", f"/in/{path.name}",
    ]
    output = subprocess.run(command, check=True, capture_output=True, text=True, encoding="utf-8").stdout
    return (json.loads(line) for line in output.splitlines() if line)
