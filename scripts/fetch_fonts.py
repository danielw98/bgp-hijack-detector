"""Vendor the page's typefaces: download the latin and latin-ext woff2 files that
Google Fonts serves for Atkinson Hyperlegible Next and Mono (400 and 700) into
src/BGPWatch.Web/wwwroot/static/fonts/, and write fonts.css with the matching @font-face rules.
Both families are published under the SIL Open Font License 1.1.

Usage: python scripts/fetch_fonts.py
"""

from __future__ import annotations

import re
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FONTS_DIR = ROOT / "src" / "BGPWatch.Web" / "wwwroot" / "static" / "fonts"
FAMILIES = ["Atkinson Hyperlegible Next", "Atkinson Hyperlegible Mono"]
WEIGHTS = "400..700"
SUBSETS = ("latin", "latin-ext")
CSS_URL = "https://fonts.googleapis.com/css2?{families}&display=swap"
BROWSER = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36"
FACE = re.compile(
    r"/\* (?P<subset>[\w-]+) \*/\s*@font-face \{(?P<body>.*?)\}", re.DOTALL
)
LICENSE = """The typefaces in this directory are Atkinson Hyperlegible Next and Atkinson
Hyperlegible Mono by the Braille Institute of America, published under the SIL Open
Font License, Version 1.1 (https://openfontlicense.org). The files were downloaded
from Google Fonts by scripts/fetch_fonts.py; fonts.css lists the source of each.
"""


def main() -> int:
    FONTS_DIR.mkdir(parents=True, exist_ok=True)
    query = "&".join(f"family={f.replace(' ', '+')}:wght@{WEIGHTS}" for f in FAMILIES)
    css = fetch(CSS_URL.format(families=query)).decode("utf-8")
    rules = []
    for face in FACE.finditer(css):
        subset, body = face.group("subset"), face.group("body")
        if subset not in SUBSETS:
            continue
        family = re.search(r"font-family: '([^']+)'", body).group(1)
        weight = re.search(r"font-weight: ([\d ]+);", body).group(1)
        url = re.search(r"url\((\S+?)\) format\('woff2'\)", body).group(1)
        unicode_range = re.search(r"unicode-range: ([^;]+);", body).group(1)
        name = f"{family.lower().replace(' ', '-')}-{subset}.woff2"
        (FONTS_DIR / name).write_bytes(fetch(url))
        rules.append(
            "/* %s, %s, %s: %s */\n@font-face {\n  font-family: '%s';\n  font-style: normal;\n"
            "  font-weight: %s;\n  font-display: swap;\n  src: url(fonts/%s) format('woff2');\n"
            "  unicode-range: %s;\n}\n" % (family, weight, subset, url, family, weight, name, unicode_range)
        )
        print(name)
    (FONTS_DIR.parent / "fonts.css").write_text("\n".join(rules), encoding="utf-8", newline="\n")
    (FONTS_DIR / "LICENSE.txt").write_text(LICENSE, encoding="utf-8", newline="\n")
    print(f"{len(rules)} faces, fonts.css written")
    return 0


def fetch(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": BROWSER})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


if __name__ == "__main__":
    raise SystemExit(main())
