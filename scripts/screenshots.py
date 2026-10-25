"""Write the README screenshots: the four views of the application and the references
page, at 1600x900 in Chromium, in English into media/ and in Romanian into media/ro/.
Read-only: no lab action.

Usage: python scripts/screenshots.py [--url https://student-dev.ro/bgp/]
Needs: pip install playwright, then python -m playwright install chromium
"""

from __future__ import annotations

import argparse
from pathlib import Path

from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
MEDIA = {"en": ROOT / "media", "ro": ROOT / "media" / "ro"}
VIEWPORT = {"width": 1600, "height": 900}
VIEWS = {"step-3": "story", "2008": "2008", "prefixes": "prefixes", "lookup": "lookup"}  # hash -> file name
SETTLE_MS = 1500
LOOKUP_MS = 30000


def shoot_app(browser, base: str, lang: str) -> None:
    page = browser.new_page(viewport=VIEWPORT)
    page.goto(f"{base}?lang={lang}", wait_until="load")
    page.wait_for_selector("svg.story-map", state="attached", timeout=20000)
    for view, name in VIEWS.items():
        page.evaluate("(hash) => { location.hash = hash; }", view)
        page.wait_for_timeout(SETTLE_MS)
        if view == "lookup":
            page.click("#btn-lookup")
            page.wait_for_selector("#lookup-result:not([hidden])", timeout=LOOKUP_MS)
            page.wait_for_timeout(SETTLE_MS)
        page.screenshot(path=str(MEDIA[lang] / f"app-{name}.png"))
    page.close()


def shoot_page(browser, url: str, path: Path) -> None:
    page = browser.new_page(viewport=VIEWPORT)
    page.goto(url, wait_until="load")
    page.wait_for_timeout(SETTLE_MS * 2)
    page.screenshot(path=str(path))
    page.close()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="https://student-dev.ro/bgp/")
    base = parser.parse_args().url.rstrip("/") + "/"
    with sync_playwright() as p:
        browser = p.chromium.launch()
        for lang, folder in MEDIA.items():
            folder.mkdir(parents=True, exist_ok=True)
            shoot_app(browser, base, lang)
            shoot_page(browser, f"{base}references?lang={lang}", folder / "references.png")
        browser.close()
    for shot in sorted(ROOT.joinpath("media").rglob("*.png")):
        print(shot.relative_to(ROOT))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
