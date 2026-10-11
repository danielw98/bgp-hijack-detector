// The simulator's map: three AS regions with a router glyph each, the BGP sessions
// between routers, the BMP reports into the collector, and one token at a time that
// travels the links. Two layouts, wide (routers in a row) and tall (routers in a
// column), chosen by the container's width; the SVG scales to its box. Labels sit in
// the quadrant of each glyph that no link leaves from, so no line crosses text.

import { esc } from "./format.js";

const ROLE_NAME = { victim: "Victimă", transit: "Tranzit", attacker: "Atacator", collector: "Colector" };
const ROUTERS = ["victim", "transit", "attacker"];
const SESSIONS = [["victim", "transit"], ["transit", "attacker"]];
const RADIUS = 30;
const TALL_BELOW = 720;
const KIND_LABEL = { update: "UPDATE", withdraw: "RETRAS", packet: "pachet", kernel: "nucleu" };
const VERDICT_LABEL = { delivered: "livrat", dropped: "aruncat", unreachable: "fără rută", lost: "pierdut", loop: "buclă" };

// Wide: labels above each router glyph, sessions on one horizontal line, reports down
// to a bus and into the collector at the bottom.
const WIDE = {
  size: [1000, 500],
  region: { victim: [20, 30, 300, 260], transit: [350, 30, 300, 260], attacker: [680, 30, 300, 260] },
  at: { victim: [170, 222], transit: [500, 222], attacker: [830, 222], collector: [500, 415] },
  text: (cx, cy, lines) => lines.map((line, i) => [cx, cy - RADIUS - 94 + i * 24, "middle", line]),
  regionLabel: ([x, y]) => [x + 16, y + 28, "start"],
  sessionLabel: ([x1, y1], [x2]) => [(x1 + x2) / 2, y1 + 36, "middle"],
  report: (role, [x, y], [cx, cy]) => {
    const top = cy - 25;
    if (role === "transit") return `M ${x} ${y + RADIUS} L ${x} ${top}`;
    const bus = 340;
    const inlet = cx + (role === "victim" ? -22 : 22);
    return `M ${x} ${y + RADIUS} L ${x} ${bus} L ${inlet} ${bus} L ${inlet} ${top}`;
  },
  collectorText: ([cx, cy]) => [[cx, cy + 54, "middle"], [cx, cy + 78, "middle"]],
};

// Tall: labels to the right of each glyph, sessions vertical, reports to a bus on the
// left and down into the collector at the bottom left.
const TALL = {
  size: [420, 1040],
  region: { victim: [100, 16, 304, 250], transit: [100, 316, 304, 250], attacker: [100, 616, 304, 250] },
  at: { victim: [170, 156], transit: [170, 456], attacker: [170, 756], collector: [60, 990] },
  text: (cx, cy, lines) => lines.map((line, i) => [cx + RADIUS + 16, cy - 30 + i * 24, "start", line]),
  regionLabel: ([x, y, w]) => [x + w - 14, y + 28, "end"],
  sessionLabel: ([x1, y1], [, y2]) => [x1 + 14, (y1 + y2) / 2 + 6, "start"],
  report: (role, [x, y], [cx, cy]) => `M ${x - RADIUS} ${y} L ${cx - 10} ${y} L ${cx - 10} ${cy - 25}`,
  collectorText: ([cx, cy]) => [[cx + 56, cy - 4, "start"], [cx + 56, cy + 20, "start"]],
};

export function createCanvas(container) {
  let layout = null;
  let lab = null;
  let token = null;
  let badge = null;
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  function pick() {
    const next = container.clientWidth < TALL_BELOW ? TALL : WIDE;
    if (next === layout) return false;
    layout = next;
    return true;
  }

  function svg() {
    return container.querySelector("svg");
  }

  function render(data) {
    lab = data;
    pick();
    const routers = Object.fromEntries(data.routers.map((r) => [r.role, r]));
    const [W, H] = layout.size;
    container.innerHTML = `<svg class="map ${layout === TALL ? "tall" : "wide"}" viewBox="0 0 ${W} ${H}" preserveAspectRatio="xMidYMid meet" role="img"
      aria-label="Simulatorul: trei sisteme autonome, sesiunile BGP dintre rutere și colectorul care le ascultă">
      <defs>
        <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M 0 0 L 10 5 L 0 10 z"></path></marker>
      </defs>
      <g class="regions">${ROUTERS.map((role) => region(role, routers[role])).join("")}</g>
      <g class="links">${links(routers)}</g>
      <g class="nodes">${ROUTERS.map((role) => router(role, routers[role])).join("")}${collector(data.collector)}</g>
      <g class="tracks"></g><g class="tokens"></g><g class="badges"></g>
    </svg>`;
    if (token) place(token.kind, token.at, token.label);
    if (badge) mark(badge.at, badge.verdict);
  }

  function region(role, r) {
    const box = layout.region[role];
    const [x, y, w, h] = box;
    const [lx, ly, anchor] = layout.regionLabel(box);
    return `<rect class="region ${role}" x="${x}" y="${y}" width="${w}" height="${h}" rx="14"></rect>
      <text class="region-label ${role}" x="${lx}" y="${ly}" text-anchor="${anchor}">${ROLE_NAME[role]} <tspan class="asn">AS${esc(r.asn)}</tspan></text>`;
  }

  function links(routers) {
    const established = (a, b) => [routers[a], routers[b]].some((r, i) =>
      r && r.sessions.some((s) => s.established && s.peer === routers[i ? a : b].address));
    const sessions = SESSIONS.map(([a, b]) => {
      const pa = layout.at[a], pb = layout.at[b];
      const up = established(a, b);
      const [lx, ly, anchor] = layout.sessionLabel(pa, pb);
      const vertical = pa[0] === pb[0];
      const d = vertical
        ? `M ${pa[0]} ${pa[1] + RADIUS} L ${pb[0]} ${pb[1] - RADIUS}`
        : `M ${pa[0] + RADIUS} ${pa[1]} L ${pb[0] - RADIUS} ${pb[1]}`;
      const text = up ? "sesiune BGP" : "sesiune căzută";
      return `<path class="session ${up ? "up" : "down"}" d="${d}" marker-start="url(#arrow)" marker-end="url(#arrow)"></path>
        ${halo(lx, ly, anchor, text)}<text class="link-label ${up ? "" : "down"}" x="${lx}" y="${ly}" text-anchor="${anchor}">${text}</text>`;
    });
    const reports = ROUTERS.map((role) => {
      const r = routers[role];
      return `<path class="report ${r.reporting ? "up" : "down"}" d="${layout.report(role, layout.at[role], layout.at.collector)}"></path>`;
    });
    return sessions.join("") + reports.join("");
  }

  // A white box behind a label that sits on a line, sized from the text length.
  function halo(x, y, anchor, text) {
    const w = text.length * 10.6 + 24;
    const left = anchor === "middle" ? x - w / 2 : anchor === "end" ? x - w + 12 : x - 12;
    return `<rect class="halo" data-halo x="${left}" y="${y - 19}" width="${w}" height="27" rx="7"></rect>`;
  }

  function router(role, r) {
    const [cx, cy] = layout.at[role];
    const lines = r.reachable
      ? [
        ["mono", r.address],
        ["detail", `${r.paths.length} ${r.paths.length === 1 ? "cale" : "căi"} în tabelă`],
        [r.reporting ? "detail up" : "detail down", r.reporting ? "raportează prin BMP" : "nu raportează"],
      ]
      : [["mono", r.address], ["detail down", "ruter oprit"], ["detail", ""]];
    const texts = layout.text(cx, cy, lines).map(([x, y, anchor, [cls, text]]) =>
      text ? `<text class="${cls}" x="${x}" y="${y}" text-anchor="${anchor}">${esc(text)}</text>` : "").join("");
    return `<g class="node ${role} ${r.reachable ? "" : "off"}" data-role="${role}">
      <circle class="ring" cx="${cx}" cy="${cy}" r="${RADIUS + 10}"></circle>
      <circle class="body" cx="${cx}" cy="${cy}" r="${RADIUS}"></circle>
      ${routerGlyph(cx, cy)}
      ${texts}
    </g>`;
  }

  // The router symbol: two arrows exchanging directions, traffic in and out.
  function routerGlyph(cx, cy) {
    const a = 15;
    return `<g class="glyph" transform="translate(${cx} ${cy})">
      <path d="M ${-a} -7 L ${a} -7" marker-end="url(#arrow)"></path>
      <path d="M ${a} 7 L ${-a} 7" marker-end="url(#arrow)"></path>
    </g>`;
  }

  function collector(c) {
    const [cx, cy] = layout.at.collector;
    const [[x1, y1, a1], [x2, y2, a2]] = layout.collectorText([cx, cy]);
    return `<g class="node collector" data-role="collector">
      <rect class="body" x="${cx - 40}" y="${cy - 25}" width="80" height="50" rx="8"></rect>
      ${[-12, 0, 12].map((dy) => `<line class="slot" x1="${cx - 26}" y1="${cy + dy}" x2="${cx + 26}" y2="${cy + dy}"></line>`).join("")}
      <text class="name" x="${x1}" y="${y1}" text-anchor="${a1}">Colector</text>
      <text class="mono" x="${x2}" y="${y2}" text-anchor="${a2}">BMP ${esc(c ? c.address : "")}</text>
    </g>`;
  }

  // The path a token follows between two nodes.
  function trackPath(from, to) {
    const a = layout.at[from], b = layout.at[to];
    if (to === "collector") return layout.report(from, a, b);
    if (from === "collector") return "";
    const direct = SESSIONS.some(([p, q]) => (p === from && q === to) || (p === to && q === from));
    if (direct) return `M ${a[0]} ${a[1]} L ${b[0]} ${b[1]}`;
    const [W] = layout.size;
    const bend = layout === TALL ? `${W - 30} ${(a[1] + b[1]) / 2}` : `${(a[0] + b[0]) / 2} ${a[1] + 120}`;
    return `M ${a[0]} ${a[1]} Q ${bend} ${b[0]} ${b[1]}`;
  }

  function pulse(role) {
    const node = svg() && svg().querySelector(`.node[data-role="${role}"]`);
    if (!node) return;
    node.classList.add("hot");
    setTimeout(() => node.classList.remove("hot"), 700);
  }

  function clearTokens() {
    token = null;
    badge = null;
    ["tracks", "tokens", "badges"].forEach((name) => { const g = svg() && svg().querySelector(`.${name}`); if (g) g.innerHTML = ""; });
  }

  // Puts the token at rest on a node.
  function place(kind, at, label) {
    const g = svg().querySelector(".tokens");
    g.innerHTML = tokenMarkup(kind, label);
    const [x, y] = layout.at[at];
    g.firstElementChild.setAttribute("transform", `translate(${x} ${y - RADIUS - 2})`);
    token = { kind, at, label };
  }

  function tokenMarkup(kind, label) {
    const text = `${KIND_LABEL[kind] || kind} ${label || ""}`.trim();
    const width = text.length * 10.5 + 26;
    return `<g class="token ${kind}"><rect x="${-width / 2}" y="-16" width="${width}" height="32" rx="16"></rect><text y="6" text-anchor="middle">${esc(text)}</text></g>`;
  }

  // A verdict next to a node: delivered, dropped, no route.
  function mark(at, verdict) {
    const g = svg().querySelector(".badges");
    const [x, y] = layout.at[at];
    const text = VERDICT_LABEL[verdict] || verdict;
    const width = text.length * 11 + 28;
    g.innerHTML = `<g class="badge ${verdict}" transform="translate(${x} ${y + RADIUS + 26})"><rect x="${-width / 2}" y="-15" width="${width}" height="30" rx="6"></rect><text y="6" text-anchor="middle">${esc(text)}</text></g>`;
    badge = { at, verdict };
  }

  // Moves the token of an event from its source to its target over `ms`, then leaves it
  // there; with no target the token sits on the source and the verdict is marked.
  function move(ev, ms) {
    svg().querySelector(".badges").innerHTML = "";
    badge = null;
    if (!ev.to) {
      place(ev.kind, ev.from, ev.label);
      if (ev.verdict) mark(ev.from, ev.verdict);
      pulse(ev.from);
      return Promise.resolve();
    }
    return new Promise((resolve) => {
      const tracks = svg().querySelector(".tracks");
      tracks.innerHTML = `<path class="track ${ev.kind}" d="${trackPath(ev.from, ev.to)}"></path>`;
      const track = tracks.firstElementChild;
      const length = track.getTotalLength();
      const g = svg().querySelector(".tokens");
      g.innerHTML = tokenMarkup(ev.kind, ev.label);
      const el = g.firstElementChild;
      const at = (t) => {
        const p = track.getPointAtLength(length * t);
        el.setAttribute("transform", `translate(${p.x} ${p.y - 24})`);
      };
      const done = () => {
        tracks.innerHTML = "";
        place(ev.kind, ev.to, ev.label);
        if (ev.verdict) mark(ev.to, ev.verdict);
        pulse(ev.to);
        resolve();
      };
      if (reduced || ms <= 0) { done(); return; }
      const start = performance.now();
      const frame = (now) => {
        const t = Math.min(1, (now - start) / ms);
        at(t);
        if (t < 1) requestAnimationFrame(frame); else done();
      };
      at(0);
      requestAnimationFrame(frame);
    });
  }

  new ResizeObserver(() => { if (lab && pick()) render(lab); }).observe(container);

  return { render, move, pulse, clearTokens };
}
