"""Write as-names.json beside an incident config: the holder name of every AS that
appears in a path announcing one of the monitored prefixes, from RIPEstat's
as-overview endpoint, with the date of the lookup.

Usage: python scripts/fetch_as_names.py detector/incidents/youtube-2008
Needs the C parser built locally: sh detector/mrt/build.sh
"""

from __future__ import annotations

import json
import sys
import urllib.request
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "detector"))

from bgpwatch.config import load_config  # noqa: E402
from bgpwatch.mrt import read_mrt  # noqa: E402

RIPESTAT = "https://stat.ripe.net/data/as-overview/data.json?resource=AS{asn}&sourceapp=ptirc-bgpwatch"


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        sys.stderr.write(__doc__)
        return 2
    incident = Path(argv[1])
    cfg = load_config(incident / "config.toml")
    monitored = [m.prefix for m in cfg.monitored]
    ases: set[int] = set()
    for path in sorted(incident.glob("updates.*")):
        for ann in read_mrt(path):
            if any(ann.prefix.version == m.version and ann.prefix.subnet_of(m) for m in monitored):
                ases.update(ann.as_path)
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
