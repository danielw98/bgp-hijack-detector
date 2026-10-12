"""Write the README screenshots into media/: the four views of the application
and the references page, at 1600x900 in Chromium. Read-only: no lab action.

Usage: python scripts/screenshots.py [--url https://student-dev.ro/bgp/]
Needs: pip install playwright, then python -m playwright install chromium
"""

from __future__ import annotations

import argparse
from pathlib import Path

from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
MEDIA = ROOT / "media"
VIEWPORT = {"width": 1600, "height": 900}
VIEWS = ("simulator", "2008", "prefixe", "verifica")
SETTLE_MS = 1500


def shoot_app(browser, base: str) -> None:
    page = browser.new_page(viewport=VIEWPORT)
    page.goto(base, wait_until="load")
    page.wait_for_selector('[data-sim="tables"] table', state="attached", timeout=20000)
    page.wait_for_selector("svg.map", state="attached", timeout=20000)
    for view in VIEWS:
        page.evaluate("(hash) => { location.hash = hash; }", view)
        page.wait_for_timeout(SETTLE_MS)
        if view == "verifica":
            page.locator("#view-verifica").get_by_role("button", name="Verifică", exact=True).click()
            page.get_by_text("se întreabă RIPEstat").wait_for(state="hidden", timeout=30000)
            page.wait_for_timeout(SETTLE_MS)
        page.screenshot(path=str(MEDIA / f"app-{view}.png"))
    page.close()


def shoot_page(browser, url: str, name: str) -> None:
    page = browser.new_page(viewport=VIEWPORT)
    page.goto(url, wait_until="load")
    page.wait_for_timeout(SETTLE_MS * 2)
    page.screenshot(path=str(MEDIA / f"{name}.png"))
    page.close()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="https://student-dev.ro/bgp/")
    base = parser.parse_args().url.rstrip("/") + "/"
    MEDIA.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch()
        shoot_app(browser, base)
        shoot_page(browser, base + "referinte", "referinte")
        browser.close()
    for shot in sorted(MEDIA.glob("*.png")):
        print(shot.relative_to(ROOT))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
