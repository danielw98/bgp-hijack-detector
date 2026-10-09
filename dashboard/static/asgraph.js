// The AS graph of a recorded incident: every AS that appears in a path announcing the
// monitored prefixes, laid out in columns by distance from the origin (origins at
// the right, the collector's peers at the left), and painted at a moment in time with
// the paths the peers then used.

import { esc, nameOf, shortName } from "./format.js";

const W = 1000;
const CHIP = { w: 104, h: 26 };
const COLUMN = 150;
const ROW = 32;
const TOP = 44;

export function createAsGraph(container, paths, highlight) {
  const nodes = new Map();   // asn -> { depth, order }
  const edges = new Map();   // "a-b" -> [a, b]
  paths.forEach((path, order) => {
    path.forEach((asn, i) => {
      const depth = path.length - 1 - i;
      const node = nodes.get(asn);
      if (!node) nodes.set(asn, { depth, order });
      else node.depth = Math.min(node.depth, depth);
    });
    for (let i = 0; i + 1 < path.length; i++) if (path[i] !== path[i + 1]) edges.set(edgeKey(path[i], path[i + 1]), [path[i], path[i + 1]]);
  });
  const columns = new Map();
  [...nodes.entries()].sort((a, b) => a[1].order - b[1].order).forEach(([asn, node]) => {
    if (!columns.has(node.depth)) columns.set(node.depth, []);
    columns.get(node.depth).push(asn);
  });
  const maxDepth = Math.max(...[...nodes.values()].map((n) => n.depth));
  const tallest = Math.max(...[...columns.values()].map((members) => members.length));
  const H = TOP + tallest * ROW + 30;
  const column = Math.min(COLUMN, (W - 140) / Math.max(1, maxDepth));
  const position = new Map();
  columns.forEach((members, depth) => {
    const x = W - 70 - depth * column;
    const gap = members.length > 1 ? (H - TOP - 60) / (members.length - 1) : 0;
    members.forEach((asn, i) => position.set(asn, [x, members.length > 1 ? TOP + i * gap : (H - CHIP.h) / 2]));
  });

  container.innerHTML = `<svg class="asgraph" viewBox="0 0 ${W} ${H}" role="img" aria-label="Sistemele autonome din căile înregistrate, de la vecinii colectorului, la stânga, la origini, la dreapta">
    <g class="edges">${[...edges.values()].map(([a, b]) => {
      const [x1, y1] = position.get(a), [x2, y2] = position.get(b);
      return `<line data-edge="${edgeKey(a, b)}" x1="${x1 + CHIP.w / 2}" y1="${y1 + CHIP.h / 2}" x2="${x2 - CHIP.w / 2}" y2="${y2 + CHIP.h / 2}"></line>`;
    }).join("")}</g>
    <g class="nodes">${[...position.entries()].map(([asn, [x, y]]) => {
      const special = highlight[asn] || "";
      const name = nameOf(asn);
      const anchor = x > W - 160 ? "end" : "middle";
      const label = special ? `<text class="holder" x="${anchor === "end" ? x + CHIP.w / 2 : x}" y="${y + CHIP.h + 15}" text-anchor="${anchor}">${esc(shortName(name).slice(0, 34))}</text>` : "";
      return `<g class="asnode ${special}" data-asn="${asn}"><title>${esc(name ? `AS${asn}, ${name}` : `AS${asn}`)}</title>
        <rect x="${x - CHIP.w / 2}" y="${y}" width="${CHIP.w}" height="${CHIP.h}" rx="4"></rect>
        <text x="${x}" y="${y + 18}" text-anchor="middle">AS${asn}</text>${label}</g>`;
    }).join("")}</g>
    <text class="axislabel" x="20" y="22">vecinii colectorului</text>
    <text class="axislabel" x="${W - 20}" y="22" text-anchor="end">originile</text>
  </svg>`;
  const svg = container.querySelector("svg");

  // Paints the paths in use at one moment: each is {path, color}.
  function paint(used) {
    svg.querySelectorAll("[data-edge], .asnode").forEach((el) => el.classList.remove("red", "green", "grey"));
    const nodeColor = new Map();
    used.forEach(({ path, color }) => {
      if (!path) return;
      for (let i = 0; i + 1 < path.length; i++) {
        const line = svg.querySelector(`[data-edge="${edgeKey(path[i], path[i + 1])}"]`);
        if (line && !line.classList.contains("red")) { line.classList.remove("green", "grey"); line.classList.add(color); }
      }
      path.forEach((asn) => { if (nodeColor.get(asn) !== "red") nodeColor.set(asn, color); });
    });
    nodeColor.forEach((color, asn) => { const n = svg.querySelector(`.asnode[data-asn="${asn}"]`); if (n) n.classList.add(color); });
  }

  function pulse(asn) {
    const n = svg.querySelector(`.asnode[data-asn="${asn}"]`);
    if (!n) return;
    n.classList.add("hot");
    setTimeout(() => n.classList.remove("hot"), 600);
  }

  return { paint, pulse, size: { nodes: nodes.size, edges: edges.size } };
}

function edgeKey(a, b) {
  return a < b ? `${a}-${b}` : `${b}-${a}`;
}
