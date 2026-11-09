"""Exports the guide's still figures: the story's map at each of its five steps, in both
languages, saved as the SVG the page drew (static/figures/step-N.<lang>.svg). The guide
inlines these files, so app.css styles them exactly as the application does.

Usage: python scripts/export_figures.py [--url http://localhost:8080/]
Needs: pip install playwright, then python -m playwright install chromium
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path

from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
FIGURES = ROOT / "src" / "BGPWatch.Web" / "wwwroot" / "static" / "figures"
STEPS = range(1, 6)
LANGUAGES = ("en", "ro")
WIDE = (1366, 768)  # the layout with the routers in a row


def main() -> None:
    parser = argparse.ArgumentParser(description="Exports the guide's still figures from the story's map.")
    parser.add_argument("--url", default="http://localhost:8080/")
    args = parser.parse_args()
    base = args.url.rstrip("/") + "/"
    FIGURES.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as pw:
        browser = pw.chromium.launch()
        page = browser.new_page(viewport={"width": WIDE[0], "height": WIDE[1]})
        for lang in LANGUAGES:
            for step in STEPS:
                page.goto(f"{base}?lang={lang}#step-{step}")
                page.wait_for_selector("svg.story-map.wide")
                page.wait_for_function("document.querySelector('.story-panel h2') !== null")
                svg = page.eval_on_selector("svg.story-map", "(el) => el.outerHTML")
                svg = re.sub(r'\bid="head"', f'id="head-{step}"', svg)
                svg = svg.replace("url(#head)", f"url(#head-{step})")
                svg = re.sub(r'\s(role|aria-label)="[^"]*"', "", svg)
                out = FIGURES / f"step-{step}.{lang}.svg"
                out.write_text(svg + "\n", encoding="utf-8", newline="\n")
                print(f"wrote {out.relative_to(ROOT)}")
        browser.close()


if __name__ == "__main__":
    main()
