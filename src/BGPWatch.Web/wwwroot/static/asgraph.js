// The AS graph of a recorded incident, reduced to what decides where traffic goes: the
// collector's peers at the left, the AS that hands each path to its origin in the
// middle, the origins at the right. An AS that plays two roles has a chip in each
// column. The ASes between a peer and its hand-off fold into one dashed link whose title
// lists them. Painted at a moment in time with the paths the peers then used; the
// links nobody uses at that moment stay hidden.

import { esc, nameOf, shortName } from "./format.js";

const W = 1000;
const CHIP = { w: 104, h: 26 };
const X = { peer: 90, handoff: 500, origin: 900 };
const ROW = 42;
const NAMED_ROW = 64;
const TOP = 50;
const NAME_CHARS = 34;

export function createAsGraph(container, paths, highlight) {
  const folds = paths.map(fold).filter(Boolean);
  const origins = unique(folds.map((f) => f.origin));
  const handoffs = unique(folds.map((f) => f.handoff));
  const peers = unique(folds.map((f) => f.peer));
  const links = new Map();   // "id-id" -> { a, b, hidden: Set of the folded-away AS lists }
  folds.forEach((f) => {
    link(links, id("peer", f.peer), id("handoff", f.handoff), f.hidden);
    link(links, id("handoff", f.handoff), id("origin", f.origin), []);
  });

  const handoffOrder = byBarycenter(handoffs.map((asn) => id("handoff", asn)), origins.map((asn) => id("origin", asn)), links);
  const peerOrder = byBarycenter(peers.map((asn) => id("peer", asn)), handoffOrder, links);
  const H = TOP + Math.max(peerOrder.length * ROW, handoffOrder.length * NAMED_ROW, origins.length * NAMED_ROW) + 20;
  const position = new Map();
  peerOrder.forEach((key, i) => position.set(key, [X.peer, TOP + i * ROW]));
  spread(handoffOrder, X.handoff, position, H);
  spread(origins.map((asn) => id("origin", asn)), X.origin, position, H);

  container.innerHTML = `<svg class="asgraph" viewBox="0 0 ${W} ${H}" role="img" aria-label="Vecinii colectorului, la stânga, sistemele autonome care predau ruta originii, la mijloc, și originile, la dreapta">
    <g class="edges">${[...links.values()].map(({ a, b, hidden }) => {
      const [x1, y1] = position.get(a), [x2, y2] = position.get(b);
      const variants = [...hidden];
      const folded = variants.some(Boolean);
      const title = folded ? `<title>între ele: ${esc(variants.map((v) => v || "niciun alt AS").join(" sau "))}</title>` : "";
      return `<line class="${folded ? "folded" : ""}" data-edge="${a}-${b}" x1="${x1 + CHIP.w / 2}" y1="${y1 + CHIP.h / 2}" x2="${x2 - CHIP.w / 2}" y2="${y2 + CHIP.h / 2}">${title}</line>`;
    }).join("")}</g>
    <g class="nodes">${[...position.entries()].map(([key, [x, y]]) => node(key, x, y, highlight[asnOf(key)] || "")).join("")}</g>
    <text class="axislabel" x="${X.peer - CHIP.w / 2}" y="22">vecinii colectorului</text>
    <text class="axislabel" x="${X.handoff}" y="22" text-anchor="middle">predă ruta originii</text>
    <text class="axislabel" x="${X.origin + CHIP.w / 2}" y="22" text-anchor="end">originile</text>
  </svg>`;
  const svg = container.querySelector("svg");

  // Paints the paths in use at one moment: each is {path, color}.
  function paint(used) {
    svg.querySelectorAll("[data-edge], .asnode").forEach((el) => el.classList.remove("red", "green", "grey"));
    const nodeColor = new Map();
    used.forEach(({ path, color }) => {
      const f = path && fold(path);
      if (!f) return;
      const keys = [id("peer", f.peer), id("handoff", f.handoff), id("origin", f.origin)];
      [[keys[0], keys[1]], [keys[1], keys[2]]].forEach(([a, b]) => {
        const line = svg.querySelector(`[data-edge="${a}-${b}"]`);
        if (line && !line.classList.contains("red")) { line.classList.remove("green", "grey"); line.classList.add(color); }
      });
      keys.forEach((key) => { if (nodeColor.get(key) !== "red") nodeColor.set(key, color); });
    });
    nodeColor.forEach((color, key) => svg.querySelector(`.asnode[data-key="${key}"]`).classList.add(color));
  }

  // Flashes the chip of a peer whose route just changed.
  function pulse(asn) {
    const n = svg.querySelector(`.asnode[data-key="${id("peer", asn)}"]`);
    if (!n) return;
    n.classList.add("hot");
    setTimeout(() => n.classList.remove("hot"), 600);
  }

  return { paint, pulse, size: { nodes: new Set(paths.flat()).size, shown: position.size } };
}

// A path without its prepending, as the peer it starts at, the AS that hands it to the
// origin, the origin, and the ASes in between.
function fold(path) {
  const hops = path.filter((asn, i) => i === 0 || asn !== path[i - 1]);
  if (hops.length < 2) return null;
  return {
    peer: hops[0],
    handoff: hops[hops.length - 2],
    origin: hops[hops.length - 1],
    hidden: hops.slice(1, -2).map((asn) => `AS${asn}`),
  };
}

function id(role, asn) {
  return `${role}${asn}`;
}

function asnOf(key) {
  return Number(key.replace(/^\D+/, ""));
}

function link(links, a, b, hidden) {
  const key = `${a}-${b}`;
  if (!links.has(key)) links.set(key, { a, b, hidden: new Set() });
  links.get(key).hidden.add(hidden.join(", "));
}

function node(key, x, y, special) {
  const asn = asnOf(key);
  const name = nameOf(asn);
  const anchor = x === X.origin ? "end" : "middle";
  const caption = x !== X.peer && name ? shortName(name).slice(0, NAME_CHARS) : "";
  const label = caption ? `<text class="holder" x="${anchor === "end" ? x + CHIP.w / 2 : x}" y="${y + CHIP.h + 15}" text-anchor="${anchor}">${esc(caption)}</text>` : "";
  return `<g class="asnode ${special}" data-key="${key}"><title>${esc(name ? `AS${asn}, ${name}` : `AS${asn}`)}</title>
    <rect x="${x - CHIP.w / 2}" y="${y}" width="${CHIP.w}" height="${CHIP.h}" rx="4"></rect>
    <text x="${x}" y="${y + 18}" text-anchor="middle">AS${asn}</text>${label}</g>`;
}

// The members ordered by the mean rank of the neighbors they link to, so links cross less.
function byBarycenter(members, neighbors, links) {
  const rank = (key) => {
    const ranks = neighbors.map((n, i) => (links.has(`${key}-${n}`) || links.has(`${n}-${key}`) ? i : -1)).filter((i) => i >= 0);
    return ranks.length ? ranks.reduce((a, b) => a + b, 0) / ranks.length : neighbors.length;
  };
  return members.map((key, i) => ({ key, rank: rank(key), i })).sort((a, b) => a.rank - b.rank || a.i - b.i).map((m) => m.key);
}

// Places a column's chips evenly over the graph's height.
function spread(keys, x, position, height) {
  const band = (height - TOP - 20) / keys.length;
  keys.forEach((key, i) => position.set(key, [x, TOP + i * band + (band - CHIP.h - 15) / 2]));
}

function unique(values) {
  return [...new Set(values)];
}
