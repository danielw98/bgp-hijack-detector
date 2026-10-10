"""Write as-names.json beside an incident config: the holder name of every AS that
appears in a path announcing one of the monitored prefixes, from RIPEstat's
as-overview endpoint, with the date of the lookup.

Usage: python scripts/fetch_as_names.py detector/incidents/youtube-2008
Needs the app image (docker compose build), whose dump verb reads the MRT files.
"""

from __future__ import annotations

import json
import sys
import tomllib
import urllib.request
from datetime import date
from ipaddress import ip_network
from pathlib import Path

from dumped_routes import dumped_routes

RIPESTAT = "https://stat.ripe.net/data/as-overview/data.json?resource=AS{asn}&sourceapp=ptirc-bgpwatch"


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        sys.stderr.write(__doc__)
        return 2
    incident = Path(argv[1])
    config = tomllib.loads((incident / "config.toml").read_text(encoding="utf-8"))
    monitored = [ip_network(m["prefix"]) for m in config.get("monitored", [])]
    ases: set[int] = set()
    for path in sorted(incident.glob("updates.*")):
        for route in dumped_routes(path):
            prefix = ip_network(route["prefix"])
            if any(prefix.version == m.version and prefix.subnet_of(m) for m in monitored):
                ases.update(route["as_path"])
    names = {str(asn): holder(asn) for asn in sorted(ases)}
    out = incident / "as-names.json"
    document = {"fetched": date.today().isoformat(), "source": "RIPEstat as-overview", "names": names}
    with open(out, "w", encoding="utf-8", newline="\n") as stream:
        json.dump(document, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    print(f"{out}: {len(names)} ASes")
    return 0


def holder(asn: int) -> str:
    with urllib.request.urlopen(RIPESTAT.format(asn=asn), timeout=30) as response:
        return json.load(response)["data"]["holder"]


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
