"""Walk the tour as a visitor and fail on anything a visitor would notice. In Chromium, in
English and Romanian, at phone, laptop and desktop sizes: a cold open shows the tour with
step 1 and a usable Play; every step shows its title and its note, the grid says what the
step teaches (step 5 drops the packet to 203.0.113.1 from every router, step 8 delivers it
from the victim and the transit while the attacker's own column stays dropped), the step 8
panel names the routes the transit rejected, nothing throws in the page; Free mode shows a
caption; the 2008 view keeps every disclosure toggle visible with one panel open and no
horizontal scroll. Read-only: no lab action, the recorded source only.

Usage: python scripts/walk_tour.py [--url http://localhost:8081/]
Needs: pip install playwright, then python -m playwright install chromium
"""

from __future__ import annotations

import argparse
import sys

from playwright.sync_api import sync_playwright

VIEWPORTS = ((390, 844), (1366, 768), (1920, 1080))
LANGUAGES = ("en", "ro")
STEPS = 8
SETTLE_MS = 2500
PLAY_MS = 9000
STEP_TITLE = {"en": {1: "A network announces its addresses", 8: "The defence: origin validation"},
              "ro": {1: "O rețea își anunță adresele", 8: "Apărarea: validarea originii"}}
DROPPED = {"en": "dropped", "ro": "aruncat"}
VICTIM = {"en": "Victim", "ro": "Victimă"}


def grid_cells(page) -> list[str]:
    return page.locator("table.grid .cell").all_inner_texts()


def walk(page, base: str, lang: str, width: int, findings: list[str]) -> None:
    where = f"{lang} {width}px"
    errors: list[str] = []
    page.on("pageerror", lambda e: errors.append(str(e)))
    page.goto(f"{base}?lang={lang}#simulator", wait_until="load")
    page.wait_for_selector("svg.map", state="attached", timeout=20000)
    page.wait_for_timeout(SETTLE_MS)
    if not page.locator(".tour-panel h3").is_visible():
        findings.append(f"{where}: the tour panel is not shown on a cold open")
    if page.locator(".tour-panel h3").inner_text() != STEP_TITLE[lang][1]:
        findings.append(f"{where}: a cold open does not show step 1 ({page.locator('.tour-panel h3').inner_text()!r})")
    if page.locator('[data-sim="play"]').is_disabled() and page.locator('[data-sim="count"]').inner_text() == "":
        findings.append(f"{where}: nothing to play on a cold open")
    for n in range(1, STEPS + 1):
        page.evaluate("(h) => { location.hash = h; }", f"#tour-{n}")
        page.wait_for_timeout(SETTLE_MS)
        title = page.locator(".tour-panel h3").inner_text()
        if not title:
            findings.append(f"{where}: step {n} shows no title")
        if page.locator('[data-tour="note"]').inner_text().strip() == "":
            findings.append(f"{where}: step {n} shows no note on the source")
        if n == 5:
            cells = grid_cells(page)
            if cells[:3] != [DROPPED[lang]] * 3:
                findings.append(f"{where}: step 5 grid does not drop the packet to .1 from every router ({cells})")
        if n == 8:
            page.wait_for_timeout(PLAY_MS)
            cells = grid_cells(page)
            if cells[0] != VICTIM[lang] or cells[1] != VICTIM[lang] or cells[2] != DROPPED[lang]:
                findings.append(f"{where}: step 8 grid is not victim, victim, dropped for .1 ({cells})")
            if "203.0.113.0/25" not in page.locator('[data-tour="extra"]').inner_text():
                findings.append(f"{where}: step 8 does not name the routes the transit rejected")
            if title != STEP_TITLE[lang][8]:
                findings.append(f"{where}: step 8 title is {title!r}")
    page.locator('[data-mode="free"]').click()
    page.wait_for_timeout(SETTLE_MS)
    if page.locator('[data-sim="caption"]').inner_text().strip() == "":
        findings.append(f"{where}: Free mode shows no caption")
    page.evaluate("() => { location.hash = '#2008'; }")
    page.wait_for_timeout(SETTLE_MS)
    page.locator('[aria-controls="more-raw-2008"]').click()
    page.wait_for_timeout(500)
    toggles = page.locator("#view-2008 .more-row .toggle")
    if any(not toggles.nth(i).is_visible() for i in range(toggles.count())):
        findings.append(f"{where}: a 2008 disclosure toggle vanished with the raw panel open")
    if page.evaluate("() => document.documentElement.scrollWidth > window.innerWidth + 1"):
        findings.append(f"{where}: horizontal page scroll with the 2008 raw panel open")
    if errors:
        findings.append(f"{where}: page errors: {errors[:3]}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="http://localhost:8081/")
    base = parser.parse_args().url.rstrip("/") + "/"
    findings: list[str] = []
    with sync_playwright() as p:
        browser = p.chromium.launch()
        for lang in LANGUAGES:
            for width, height in VIEWPORTS:
                page = browser.new_page(viewport={"width": width, "height": height})
                walk(page, base, lang, width, findings)
                page.close()
        browser.close()
    for finding in findings:
        print(finding)
    print("ok" if not findings else f"{len(findings)} findings")
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
