"""Export the guide's still figures from the simulator: load the local app with GET
/api/lab answered by a fixture lab state (scripts/figures/<state>.json) and an empty route
log, then save either the map (svg.map, after an optional packet walk) or some rows of
the router tables as one standalone SVG per language into
src/BGPWatch.Web/wwwroot/static/figures/<name>.<lang>.svg. Each file carries its styles
inline and its fonts as subset data URIs, so it renders on its own inside an <img>.
Map labels are scaled about their anchors so every text reads at MIN_PX or more when
the figure is COLUMN px wide; the export fails otherwise. Read-only: no lab action.

The fixtures come from the live lab (GET /api/lab at rest, after POST /api/lab/trigger
with {"scenario": "exact"} and with {"scenario": "subprefix"}); defence.json is written
by hand from the tables expected with origin validation on at the transit.

Usage: python scripts/export_figures.py [--url http://localhost:8081/]
Needs: pip install playwright fonttools brotli, then python -m playwright install chromium
"""

from __future__ import annotations

import argparse
import base64
import io
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
STATIC = ROOT / "src" / "BGPWatch.Web" / "wwwroot" / "static"
FIXTURES = ROOT / "scripts" / "figures"
OUT = STATIC / "figures"
LANGUAGES = ("en", "ro")
VIEWPORT = {"width": 1366, "height": 768}
COLUMN = 760
MIN_PX = 15.5
TABLE_WIDTH = 600
SETTLE_MS = 1200


@dataclass(frozen=True)
class Figure:
    name: str
    state: str
    kind: str  # "map" or "table"
    packet: tuple[str, str] | None = None  # (from role, to address) walked before the export
    routers: tuple[str, ...] = ()
    prefixes: tuple[str, ...] = field(default=())


FIGURES = (
    Figure("rest", "rest", "map"),
    Figure("path", "rest", "table", routers=("victim", "transit", "attacker"), prefixes=("203.0.113.0/24",)),
    Figure("packet", "rest", "map", packet=("attacker", "203.0.113.1")),
    Figure("exact", "exact", "table", routers=("transit",)),
    Figure("subprefix", "subprefix", "map", packet=("attacker", "203.0.113.1")),
    Figure("defence", "defence", "table", routers=("transit", "attacker")),
)

# Clones svg.map with its computed paint and type inlined, scales every label about its
# anchor (tokens and badges as whole groups) so the smallest text reaches minPx at the
# column width, crops the viewBox to the drawing and returns the markup with its texts.
MAP_JS = """
({ column, minPx }) => {
  const PROPS = ['fill', 'fill-opacity', 'stroke', 'stroke-width', 'stroke-dasharray', 'stroke-linecap',
    'stroke-linejoin', 'stroke-opacity', 'opacity', 'font-family', 'font-size', 'font-weight', 'font-style',
    'text-anchor', 'dominant-baseline', 'letter-spacing', 'paint-order'];
  const live = document.querySelector('svg.map');
  const svg = live.cloneNode(true);
  const sources = [live, ...live.querySelectorAll('*')];
  [svg, ...svg.querySelectorAll('*')].forEach((el, i) => {
    const cs = getComputedStyle(sources[i]);
    el.setAttribute('style', PROPS.map((p) => `${p}:${cs.getPropertyValue(p)}`).join(';'));
    ['tabindex', 'role', 'aria-label', 'data-role', 'data-halo'].forEach((a) => el.removeAttribute(a));
  });
  svg.querySelectorAll('title').forEach((t) => t.remove());
  svg.setAttribute('role', 'img');
  const host = document.createElement('div');
  host.style.cssText = 'position:absolute;left:0;top:0;width:1000px;height:500px';
  document.body.append(host);
  host.append(svg);
  const texts = [...svg.querySelectorAll('text')].filter((t) => t.textContent.trim());
  const smallest = Math.min(...texts.map((t) => parseFloat(t.style.fontSize)));
  const crop = (pad) => { const b = svg.getBBox(); return [b.x - pad, b.y - pad, b.width + 2 * pad, b.height + 2 * pad]; };
  const scale = (minPx * crop(12)[2] * 1.06) / (column * smallest);
  const k = Math.max(1, scale);
  const about = (x, y) => `translate(${x} ${y}) scale(${k}) translate(${-x} ${-y})`;
  svg.querySelectorAll('.token, .badge').forEach((g) => g.setAttribute('transform', `${g.getAttribute('transform') || ''} scale(${k})`));
  texts.filter((t) => !t.closest('.token, .badge')).forEach((t) => {
    const x = t.x.baseVal[0].value, y = t.y.baseVal[0].value;
    t.setAttribute('transform', about(x, y));
    const halo = t.previousElementSibling;
    if (halo && halo.classList.contains('halo')) halo.setAttribute('transform', about(x, y));
  });
  const box = crop(12);
  svg.setAttribute('viewBox', box.map((v) => v.toFixed(1)).join(' '));
  svg.setAttribute('width', Math.round(box[2]));
  svg.setAttribute('height', Math.round(box[3]));
  svg.removeAttribute('class');
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  const shown = smallest * k * column / box[2];
  const markup = new XMLSerializer().serializeToString(svg);
  host.remove();
  return { markup, text: svg.textContent, shown, width: Math.round(box[2]), height: Math.round(box[3]) };
}
"""

# Copies the chosen routers' rows of the router tables into a table of a fixed width,
# inlines every computed style that differs from a blank element of the same tag, and
# returns it as XHTML with its size.
TABLE_JS = """
({ routers, prefixes, width }) => {
  const SKIP = /^(transition|animation|cursor|will-change|pointer-events|-webkit-tap|caret|outline|user-select)/;
  const blank = document.createElement('iframe');
  blank.style.cssText = 'position:absolute;width:0;height:0;border:0';
  document.body.append(blank);
  const defaults = new Map();
  const base = (tag) => {
    if (!defaults.has(tag)) {
      const el = blank.contentDocument.createElement(tag);
      blank.contentDocument.body.append(el);
      const cs = blank.contentWindow.getComputedStyle(el);
      defaults.set(tag, Object.fromEntries([...cs].map((p) => [p, cs.getPropertyValue(p)])));
    }
    return defaults.get(tag);
  };
  const source = document.querySelector('[data-sim="tables"] table');
  const table = source.cloneNode(false);
  table.append(source.tHead.cloneNode(true));
  routers.forEach((role) => {
    const body = source.querySelector(`tbody.${role}`).cloneNode(true);
    if (prefixes.length) body.querySelectorAll('tr[data-prefix]').forEach((tr) => { if (!prefixes.includes(tr.dataset.prefix)) tr.remove(); });
    table.append(body);
  });
  const host = document.createElement('div');
  host.className = 'figure-export';
  host.style.cssText = `position:absolute;left:0;top:0;width:${width}px;background:#ffffff`;
  host.append(table);
  document.body.append(host);
  const copy = host.cloneNode(true);
  const live = [host, ...host.querySelectorAll('*')];
  [copy, ...copy.querySelectorAll('*')].forEach((el, i) => {
    const cs = getComputedStyle(live[i]);
    const ref = base(el.tagName.toLowerCase());
    const style = [...cs].filter((p) => !SKIP.test(p) && cs.getPropertyValue(p) !== ref[p])
      .map((p) => `${p}:${cs.getPropertyValue(p)}`).join(';');
    el.setAttribute('style', style);
    el.removeAttribute('class');
    Object.keys(el.dataset).forEach((k) => delete el.dataset[k]);
  });
  copy.style.position = 'static';
  copy.setAttribute('xmlns', 'http://www.w3.org/1999/xhtml');
  const height = Math.ceil(host.getBoundingClientRect().height);
  const markup = new XMLSerializer().serializeToString(copy);
  host.remove();
  blank.remove();
  return { markup, text: host.textContent, width, height };
}
"""


def font_faces() -> list[dict]:
    css = (STATIC / "fonts.css").read_text(encoding="utf-8")
    faces = []
    for block in re.findall(r"@font-face\s*\{(.*?)\}", css, re.S):
        faces.append({
            "family": re.search(r"font-family:\s*'([^']+)'", block).group(1),
            "weight": re.search(r"font-weight:\s*([^;]+);", block).group(1).strip(),
            "file": STATIC / re.search(r"url\(([^)]+)\)", block).group(1),
            "range": re.search(r"unicode-range:\s*([^;]+);", block).group(1).strip(),
        })
    return faces


def embedded_fonts(text: str) -> str:
    """The @font-face rules of fonts.css with each file subset to the figure's characters."""
    rules = []
    for face in font_faces():
        cmap = TTFont(face["file"]).getBestCmap()
        chars = "".join(sorted({c for c in text if ord(c) in cmap and not c.isspace()}))
        if not chars:
            continue
        options = subset.Options()
        options.flavor = "woff2"
        options.layout_features = ["*"]
        font = subset.load_font(str(face["file"]), options)
        subsetter = subset.Subsetter(options)
        subsetter.populate(text=chars + " ")
        subsetter.subset(font)
        buffer = io.BytesIO()
        subset.save_font(font, buffer, options)
        data = base64.b64encode(buffer.getvalue()).decode("ascii")
        rules.append(
            f"@font-face{{font-family:'{face['family']}';font-weight:{face['weight']};"
            f"src:url(data:font/woff2;base64,{data}) format('woff2');unicode-range:{face['range']}}}"
        )
    return "".join(rules)


def standalone(figure: Figure, result: dict) -> str:
    style = f"<style>{embedded_fonts(result['text'])}</style>"
    if figure.kind == "map":
        return result["markup"].replace(">", ">" + style, 1)
    w, h = result["width"], result["height"]
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}" role="img">'
        f'{style}<foreignObject x="0" y="0" width="{w}" height="{h}">{result["markup"]}</foreignObject></svg>'
    )


def export(browser, base: str, lang: str, figure: Figure) -> str:
    page = browser.new_page(viewport=VIEWPORT, reduced_motion="reduce")
    state = (FIXTURES / f"{figure.state}.json").read_text(encoding="utf-8")
    page.route(re.compile(r".*/api/lab(\?.*)?$"), lambda route: route.fulfill(status=200, content_type="application/json", body=state))
    page.route(re.compile(r".*/api/lab/events(\?.*)?$"), lambda route: route.fulfill(status=200, content_type="application/json", body="[]"))
    page.goto(f"{base}?lang={lang}#simulator", wait_until="load")
    page.wait_for_selector('[data-sim="tables"] table', state="attached", timeout=20000)
    page.wait_for_selector("svg.map", state="attached", timeout=20000)
    page.wait_for_timeout(SETTLE_MS)
    if figure.packet:
        source, target = figure.packet
        page.select_option('[data-sim="from"]', source)
        page.select_option('[data-sim="to"]', target)
        page.click('[data-sim="send"]')
        page.wait_for_selector("svg.map .badges .badge", state="attached", timeout=20000)
        page.wait_for_timeout(SETTLE_MS)
    if figure.kind == "map":
        result = page.evaluate(MAP_JS, {"column": COLUMN, "minPx": MIN_PX})
        if result["shown"] < MIN_PX - 0.01:
            raise SystemExit(f"{figure.name}.{lang}: smallest text {result['shown']:.1f}px at {COLUMN}px")
    else:
        result = page.evaluate(TABLE_JS, {"routers": list(figure.routers), "prefixes": list(figure.prefixes), "width": TABLE_WIDTH})
    page.close()
    path = OUT / f"{figure.name}.{lang}.svg"
    path.write_text(standalone(figure, result), encoding="utf-8")
    return f"{path.relative_to(ROOT)} {result['width']}x{result['height']}"


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://localhost:8081/")
    base = parser.parse_args().url.rstrip("/") + "/"
    OUT.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch()
        for lang in LANGUAGES:
            for figure in FIGURES:
                print(export(browser, base, lang, figure))
        browser.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
