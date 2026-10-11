"""Gate for the page. Loads the application and the guide at phone, laptop and desktop
sizes in Chromium and fails on: horizontal page overflow or an element wider than the
viewport outside an inner scroller; vertical page scroll of the application at laptop
and desktop sizes (the simulator fills the window); any text of the simulator's map
rendered under MIN_FONT px; any map text crossed by a line, a path or the edge of a box
(a label on a white halo may cover a line). On the references page, with every block
closed and with every block open: no horizontal overflow, also inside its scroller, and
every in-page link has its target. Also checks the colour tokens of page.css
for 4.5:1 contrast, refuses emoji in every tracked text file and non-ASCII in code
comments, and writes one full-page screenshot per size into .playwright-mcp/ (the
references page open at its full height).
Exits 1 on any finding.

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
STATIC = ROOT / "src" / "BGPWatch.Web" / "wwwroot" / "static"
SHOTS = ROOT / ".playwright-mcp"
VIEWPORTS = ((390, 844), (1366, 768), (1920, 1080))
WORKSPACE_FROM = 1000
ONE_SCREEN_VIEWS = ("2008", "prefixe", "verifica")  # views that fit one screen from WORKSPACE_FROM up
VIEWS = ("simulator", "2008", "prefixe", "verifica")
SCROLLERS = ".scroll-x, .rib, .frame-body.tight, .graphbox, .sim-inspector .panel, .evlist, .view-body, .sim-canvas"
MIN_CONTRAST = 4.5
MIN_FONT = 15.0
TEXT_SUFFIXES = {
    ".md", ".html", ".cshtml", ".js", ".css", ".py", ".cs", ".csproj", ".slnx", ".props", ".c", ".h",
    ".tex", ".toml", ".sql", ".sh", ".conf", ".bat", ".txt", ".yml", ".json",
}
CODE_SUFFIXES = {".js", ".css", ".py", ".cs", ".c", ".h"}
EMOJI = re.compile(
    "[\U0001F000-\U0001FAFF\U00002600-\U000027BF\U0001F900-\U0001F9FF\U0001FA70-\U0001FAFF\uFE0F\u2B50\u2B55]"
)
COMMENT = re.compile(r"^\s*(//|#(?!!)|/\*|\*)")

OVERFLOW_JS = """
(scrollers) => {
  const w = window.innerWidth;
  const wide = [...document.querySelectorAll('body *')].filter((el) => {
    const r = el.getBoundingClientRect();
    return r.right > w + 1 && !el.closest(scrollers);
  }).map((el) => el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + ' right=' + Math.round(el.getBoundingClientRect().right));
  return { scrollWidth: document.documentElement.scrollWidth, scrollHeight: document.documentElement.scrollHeight,
           innerWidth: w, innerHeight: window.innerHeight, wide: wide.slice(0, 10) };
}
"""

# Every text of the map: its rendered size, and whether a line, a path or a box edge
# crosses it. A text on a halo (the rect before it, data-halo) must sit inside the halo.
MAP_TEXT_JS = """
(minFont) => {
  const out = [];
  const seen = (el) => el.closest('.token, .badge, defs');
  const shrink = (r, by) => ({ left: r.left + by, top: r.top + by, right: r.right - by, bottom: r.bottom - by });
  const inside = (p, r) => p.x >= r.left && p.x <= r.right && p.y >= r.top && p.y <= r.bottom;
  const contains = (outer, inner) => inner.left >= outer.left - 1 && inner.right <= outer.right + 1 && inner.top >= outer.top - 1 && inner.bottom <= outer.bottom + 1;
  const segmentHits = (a, b, r) => {
    for (let i = 0; i <= 40; i++) { const t = i / 40; if (inside({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t }, r)) return true; }
    return false;
  };
  const toClient = (svg, x, y) => { const p = svg.createSVGPoint(); p.x = x; p.y = y; return p.matrixTransform(svg.getScreenCTM()); };
  document.querySelectorAll('svg.map').forEach((svg) => {
    const vb = svg.viewBox.baseVal;
    const box = svg.getBoundingClientRect();
    const scale = Math.min(box.width / vb.width, box.height / vb.height);
    const texts = [...svg.querySelectorAll('text, tspan')].filter((t) => !seen(t) && t.textContent.trim());
    texts.forEach((t) => {
      const px = parseFloat(getComputedStyle(t).fontSize) * scale;
      if (px < minFont) out.push(`text "${t.textContent.trim()}" renders at ${px.toFixed(1)}px`);
    });
    const labels = [...svg.querySelectorAll('text')].filter((t) => !seen(t) && t.textContent.trim());
    const lines = [...svg.querySelectorAll('line')].filter((l) => !seen(l));
    const paths = [...svg.querySelectorAll('path')].filter((p) => !seen(p) && !p.closest('.glyph, .tracks'));
    const rects = [...svg.querySelectorAll('rect')].filter((r) => !seen(r));
    const circles = [...svg.querySelectorAll('circle.body')];
    labels.forEach((t) => {
      const r = shrink(t.getBoundingClientRect(), 1);
      const halo = t.previousElementSibling && t.previousElementSibling.hasAttribute('data-halo') ? t.previousElementSibling : null;
      const name = `text "${t.textContent.trim()}"`;
      if (halo) { if (!contains(halo.getBoundingClientRect(), r)) out.push(`${name} spills out of its halo`); return; }
      lines.forEach((l) => {
        const a = toClient(svg, l.x1.baseVal.value, l.y1.baseVal.value), b = toClient(svg, l.x2.baseVal.value, l.y2.baseVal.value);
        if (segmentHits(a, b, r)) out.push(`${name} is crossed by a line`);
      });
      paths.forEach((p) => {
        const len = p.getTotalLength();
        for (let d = 0; d <= len; d += 3) { const q = p.getPointAtLength(d); if (inside(toClient(svg, q.x, q.y), r)) { out.push(`${name} is crossed by path ${p.getAttribute('class') || ''}`); break; } }
      });
      rects.forEach((b) => {
        const x = b.x.baseVal.value, y = b.y.baseVal.value, w = b.width.baseVal.value, h = b.height.baseVal.value;
        const c = [[x, y], [x + w, y], [x + w, y + h], [x, y + h]].map(([px, py]) => toClient(svg, px, py));
        for (let i = 0; i < 4; i++) if (segmentHits(c[i], c[(i + 1) % 4], r)) { out.push(`${name} is crossed by the edge of rect ${b.getAttribute('class') || ''}`); break; }
      });
      circles.forEach((c) => {
        const cx = c.cx.baseVal.value, cy = c.cy.baseVal.value, rad = c.r.baseVal.value;
        for (let i = 0; i < 64; i++) { const a = (i / 64) * Math.PI * 2; if (inside(toClient(svg, cx + rad * Math.cos(a), cy + rad * Math.sin(a)), r)) { out.push(`${name} is crossed by a router circle`); break; } }
      });
    });
  });
  return [...new Set(out)];
}
"""

# Opens every block of the references page; returns the in-page links without a target,
# whether its scroller overflows sideways, and the window height that shows it all.
REFERENCES_JS = """
() => {
  document.querySelectorAll('details').forEach((block) => { block.open = true; });
  const ids = [...document.querySelectorAll('a[href^="#"]')].map((a) => decodeURIComponent(a.getAttribute('href').slice(1)));
  const body = document.querySelector('.view-body');
  return { missing: [...new Set(ids.filter((id) => id && !document.getElementById(id)))],
           sideways: body.scrollWidth > body.clientWidth,
           height: Math.ceil(body.getBoundingClientRect().top + body.scrollHeight) };
}
"""


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://localhost:8080/")
    args = parser.parse_args()
    findings = check_tokens() + check_text()
    for name, path in (("app", ""), ("ghid", "ghid")):
        findings += check_layout(args.url.rstrip("/") + "/" + path, name)
    findings += check_references(args.url.rstrip("/") + "/referinte")
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
    pairs = [(text, bg) for text in ("ink", "muted", "green", "red", "blue", "amber") for bg in ("paper", "surface", "canvas")]
    pairs += [("surface", role) for role in ("green", "red", "blue", "amber", "ink")]
    pairs += [(text, bg) for text in ("on-ink-text", "on-ink-muted") for bg in ("ink", "on-ink-hover")]
    pairs += [("muted", "on-ink-text")]
    out = []
    for text, bg in pairs:
        ratio = contrast(t[text], t[bg])
        if ratio < MIN_CONTRAST:
            out.append(f"contrast --{text} on --{bg} is {ratio:.2f}, below {MIN_CONTRAST}")
    return out


def tracked_files() -> list[Path]:
    listed = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, check=True).stdout
    return [ROOT / line for line in listed.splitlines() if Path(line).suffix in TEXT_SUFFIXES and (ROOT / line).exists()]


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
        for width, height in VIEWPORTS:
            page = browser.new_page(viewport={"width": width, "height": height})
            page.goto(url, wait_until="load")
            page.wait_for_selector('[data-sim="tables"] table', state="attached", timeout=15000)
            page.wait_for_selector("svg.map", state="attached", timeout=15000)
            if name == "ghid":
                page.wait_for_selector(".lane-row svg", state="attached", timeout=15000)
            page.wait_for_timeout(800)
            for view in (VIEWS if name == "app" else ("",)):
                if view:
                    page.evaluate("(hash) => { location.hash = hash; }", view)
                    page.wait_for_timeout(600)
                label = f"{name}{' #' + view if view else ''} {width}px"
                result = page.evaluate(OVERFLOW_JS, SCROLLERS)
                if result["scrollWidth"] > result["innerWidth"]:
                    out.append(f"{label}: page scrolls horizontally ({result['scrollWidth']} > {result['innerWidth']})")
                if view == "simulator" and width >= WORKSPACE_FROM and result["scrollHeight"] > result["innerHeight"] + 1:
                    out.append(f"{label}: page scrolls vertically ({result['scrollHeight']} > {result['innerHeight']})")
                if view in ONE_SCREEN_VIEWS and width >= WORKSPACE_FROM:
                    body = page.evaluate("(v) => { const b = document.querySelector('#view-' + v + ' .view-body'); return [b.scrollHeight, b.clientHeight]; }", view)
                    if body[0] > body[1] + 1:
                        out.append(f"{label}: the view scrolls ({body[0]} > {body[1]})")
                for element in result["wide"]:
                    out.append(f"{label}: {element} is wider than the viewport")
                if view in ("simulator", ""):
                    for finding in page.evaluate(MAP_TEXT_JS, MIN_FONT):
                        out.append(f"{label}: {finding}")
                page.screenshot(path=str(SHOTS / f"check-{name}{'-' + view if view else ''}-{width}.png"), full_page=True)
            page.close()
        browser.close()
    return out


def check_references(url: str) -> list[str]:
    from playwright.sync_api import sync_playwright

    SHOTS.mkdir(exist_ok=True)
    out = []
    with sync_playwright() as p:
        browser = p.chromium.launch()
        for width, height in VIEWPORTS:
            page = browser.new_page(viewport={"width": width, "height": height})
            page.goto(url, wait_until="load")
            page.wait_for_selector(".refs-stage", state="attached", timeout=15000)
            for state in ("closed", "open"):
                label = f"referinte {state} {width}px"
                if state == "open":
                    opened = page.evaluate(REFERENCES_JS)
                    out += [f"{label}: link #{target} has no target" for target in opened["missing"]]
                    if opened["sideways"]:
                        out.append(f"{label}: the page body scrolls sideways")
                    page.set_viewport_size({"width": width, "height": max(height, opened["height"] + 40)})
                    page.wait_for_timeout(300)
                result = page.evaluate(OVERFLOW_JS, ".scroll-x")
                if result["scrollWidth"] > result["innerWidth"]:
                    out.append(f"{label}: page scrolls horizontally ({result['scrollWidth']} > {result['innerWidth']})")
                for element in result["wide"]:
                    out.append(f"{label}: {element} is wider than the viewport")
                page.screenshot(path=str(SHOTS / f"check-referinte-{state}-{width}.png"), full_page=True)
            page.close()
        browser.close()
    return out


if __name__ == "__main__":
    raise SystemExit(main())
