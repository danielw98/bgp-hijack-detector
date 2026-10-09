"""Gate for the page. Loads it at 390, 1366 and 1920 px in Chromium and fails on any
horizontal page overflow or any element wider than the viewport outside an inner
scroller; checks the colour tokens of page.css for 4.5:1 contrast on the paper and on
the white surface, and white text on the role colours; refuses emoji in every tracked
text file and non-ASCII in code comments; writes one full-page screenshot per width
into .playwright-mcp/. Exits 1 on any finding.

Usage: python scripts/check_page.py [--url http://localhost:8080/]
Needs: pip install playwright, then python -m playwright install chromium
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STATIC = ROOT / "dashboard" / "static"
SHOTS = ROOT / ".playwright-mcp"
WIDTHS = (390, 1366, 1920)
SCROLLERS = ".scroll-x, .rib, .frame-body.tight, .graphbox, #graph"
MIN_CONTRAST = 4.5
TEXT_SUFFIXES = {".md", ".html", ".js", ".css", ".py", ".toml", ".sql", ".sh", ".conf", ".bat", ".txt", ".yml", ".json"}
CODE_SUFFIXES = {".js", ".css", ".py"}
EMOJI = re.compile(
    "[\U0001F000-\U0001FAFF\U00002600-\U000027BF\U0001F900-\U0001F9FF\U0001FA70-\U0001FAFF️⭐⭕]"
)
COMMENT = re.compile(r"^\s*(//|#(?!!)|/\*|\*)")

OVERFLOW_JS = """
(scrollers) => {
  const w = window.innerWidth;
  const wide = [...document.querySelectorAll('body *')].filter((el) => {
    const r = el.getBoundingClientRect();
    return r.right > w + 1 && !el.closest(scrollers);
  }).map((el) => el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + ' right=' + Math.round(el.getBoundingClientRect().right));
  return { scrollWidth: document.documentElement.scrollWidth, innerWidth: w, wide: wide.slice(0, 10) };
}
"""


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://localhost:8080/")
    args = parser.parse_args()
    findings = check_tokens() + check_text()
    for name, path in (("app", ""), ("ghid", "ghid")):
        findings += check_layout(args.url.rstrip("/") + "/" + path, name)
    for finding in findings:
        print("FAIL", finding)
    print("ok" if not findings else f"{len(findings)} findings")
    return 1 if findings else 0


def tokens() -> dict[str, str]:
    css = (STATIC / "page.css").read_text(encoding="utf-8")
    root = css[css.index(":root {") : css.index("}", css.index(":root {"))]
    return dict(re.findall(r"--([a-z-]+):\s*(#[0-9a-fA-F]{6})", root))


def luminance(hex_color: str) -> float:
    channels = []
    for i in (1, 3, 5):
        c = int(hex_color[i : i + 2], 16) / 255
        channels.append(c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4)
    r, g, b = channels
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(a: str, b: str) -> float:
    la, lb = luminance(a), luminance(b)
    bright, dark = max(la, lb), min(la, lb)
    return (bright + 0.05) / (dark + 0.05)


def check_tokens() -> list[str]:
    t = tokens()
    pairs = [(text, bg) for text in ("ink", "muted", "green", "red", "blue") for bg in ("paper", "surface")]
    pairs += [("surface", role) for role in ("green", "red", "blue", "ink")]
    out = []
    for text, bg in pairs:
        ratio = contrast(t[text], t[bg])
        if ratio < MIN_CONTRAST:
            out.append(f"contrast --{text} on --{bg} is {ratio:.2f}, below {MIN_CONTRAST}")
    return out


def tracked_files() -> list[Path]:
    listed = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, check=True).stdout
    return [ROOT / line for line in listed.splitlines() if Path(line).suffix in TEXT_SUFFIXES]


def check_text() -> list[str]:
    out = []
    for path in tracked_files():
        try:
            lines = path.read_text(encoding="utf-8").splitlines()
        except UnicodeDecodeError:
            out.append(f"{path.relative_to(ROOT)} is not UTF-8")
            continue
        for number, line in enumerate(lines, 1):
            if EMOJI.search(line):
                out.append(f"{path.relative_to(ROOT)}:{number} has an emoji")
            if path.suffix in CODE_SUFFIXES and COMMENT.match(line) and not line.isascii():
                out.append(f"{path.relative_to(ROOT)}:{number} has a non-ASCII comment")
    return out


def check_layout(url: str, name: str) -> list[str]:
    from playwright.sync_api import sync_playwright

    SHOTS.mkdir(exist_ok=True)
    out = []
    with sync_playwright() as p:
        browser = p.chromium.launch()
        for width in WIDTHS:
            page = browser.new_page(viewport={"width": width, "height": 900})
            page.goto(url, wait_until="load")
            page.wait_for_selector("#ribs table", state="attached", timeout=15000)
            page.wait_for_selector("svg.lanes", state="attached", timeout=15000)
            page.wait_for_timeout(500)
            result = page.evaluate(OVERFLOW_JS, SCROLLERS)
            if result["scrollWidth"] > result["innerWidth"]:
                out.append(f"{width}px: page scrolls horizontally ({result['scrollWidth']} > {result['innerWidth']})")
            for element in result["wide"]:
                out.append(f"{width}px: {element} is wider than the viewport")
            page.screenshot(path=str(SHOTS / f"check-{name}-{width}.png"), full_page=True)
            page.close()
        browser.close()
    return out


if __name__ == "__main__":
    raise SystemExit(main())
