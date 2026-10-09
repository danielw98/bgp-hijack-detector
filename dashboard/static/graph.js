// The lab drawn as a graph: one box per AS with its router inside, the BGP sessions
// between routers, the collector listening to all three, and tokens (UPDATE messages,
// packets) that travel the edges. Positions are fixed; the SVG scales with its box.

import { esc } from "./format.js";

const W = 1000;
const H = 430;
const NODE = { w: 200, h: 86 };
const LAYOUT = {
  victim: { box: [30, 40, 285, 195], at: [172, 150] },
  transit: { box: [357, 40, 285, 195], at: [500, 150] },
  attacker: { box: [685, 40, 285, 195], at: [828, 150] },
  collector: { box: [357, 300, 285, 105], at: [500, 358] },
};
const SESSIONS = [["victim", "transit"], ["transit", "attacker"]];
const TAP_X = { victim: 445, transit: 500, attacker: 555 };
const ROLE_NAME = { victim: "Victimă", transit: "Tranzit", attacker: "Atacator", collector: "Colector" };
const HOP_MS = 850;

export function createGraph(container) {
  container.innerHTML = `<svg class="graph" viewBox="0 0 ${W} ${H}" role="img" aria-label="Laboratorul: trei sisteme autonome, sesiunile BGP dintre ele și colectorul">
    <g class="boxes"></g><g class="edges"></g><g class="nodes"></g><g class="paths"></g><g class="tokens"></g></svg>`;
  const svg = container.querySelector("svg");
  const layer = (name) => svg.querySelector(`.${name}`);
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  function render(lab) {
    const routers = Object.fromEntries(lab.routers.map((r) => [r.role, r]));
    const ids = {
      victim: `AS${routers.victim.asn}`,
      transit: `AS${routers.transit.asn}`,
      attacker: `AS${routers.attacker.asn}`,
      collector: `AS${lab.collector.asn}`,
    };
    layer("boxes").innerHTML = Object.entries(LAYOUT).map(([role, l]) => {
      const [x, y, w, h] = l.box;
      return `<rect class="asbox ${role}" x="${x}" y="${y}" width="${w}" height="${h}" rx="8"></rect>
        <text class="aslabel ${role}" x="${x + 14}" y="${y + 26}"><tspan class="asn">${ids[role]}</tspan> ${ROLE_NAME[role]}</text>`;
    }).join("");

    const established = (a, b) => {
      const ra = routers[a], rb = routers[b];
      return [ra, rb].some((r) => r && r.sessions.some((s) => s.established && (s.peer === (r === ra ? rb.address : ra.address))));
    };
    const sessions = SESSIONS.map(([a, b]) => {
      const [x1, y1] = LAYOUT[a].at, [x2, y2] = LAYOUT[b].at;
      const up = established(a, b);
      return `<line class="edge ${up ? "up" : "down"}" x1="${x1 + NODE.w / 2}" y1="${y1}" x2="${x2 - NODE.w / 2}" y2="${y2}"></line>
        <text class="edgelabel ${up ? "up" : "down"}" x="${(x1 + x2) / 2}" y="${y1 - 10}" text-anchor="middle">${up ? "sesiune BGP" : "sesiune căzută"}</text>`;
    });
    const taps = ["victim", "transit", "attacker"].map((role) => {
      const r = routers[role];
      const up = r.sessions.some((s) => s.peer === lab.collector.address && s.established);
      const [x, y] = LAYOUT[role].at;
      return `<line class="tap ${up ? "up" : "down"}" x1="${x}" y1="${y + NODE.h / 2}" x2="${TAP_X[role]}" y2="${LAYOUT.collector.at[1] - LAYOUT.collector.box[3] / 2 + 36}"></line>`;
    });
    layer("edges").innerHTML = sessions.join("") + taps.join("") +
      `<text class="edgelabel" x="500" y="${H - 6}" text-anchor="middle">colectorul ascultă fiecare ruter și dă detectorului fiecare mesaj</text>`;

    layer("nodes").innerHTML = Object.entries(LAYOUT).map(([role, l]) => {
      const [cx, cy] = l.at;
      const r = role === "collector" ? { address: lab.collector.address, reachable: true, paths: [], hosts: [] } : routers[role];
      const h = role === "collector" ? 52 : NODE.h;
      const lines = role === "collector"
        ? []
        : (r.reachable
          ? [`${r.paths.length} ${r.paths.length === 1 ? "cale" : "căi"} în tabelă`, r.hosts.length ? `gazde: ${r.hosts.map((a, i) => (i ? a.replace(/^.*\.(?=\d+$)/, ".") : a)).join(", ")}` : ""]
          : ["ruter oprit"]);
      const detail = lines.filter(Boolean).map((text, i) => `<text class="detail" x="${cx}" y="${cy + 10 + i * 19}" text-anchor="middle">${esc(text)}</text>`).join("");
      return `<g class="node ${role} ${r.reachable ? "" : "off"}" data-role="${role}">
        <rect x="${cx - NODE.w / 2}" y="${cy - h / 2}" width="${NODE.w}" height="${h}" rx="6"></rect>
        <text class="name" x="${cx}" y="${cy - (role === "collector" ? 2 : 14)}" text-anchor="middle">${ROLE_NAME[role]} <tspan class="addr">${esc(r.address)}</tspan></text>
        ${detail}
      </g>`;
    }).join("");
  }

  function edgePath(from, to) {
    const [x1, y1] = LAYOUT[from].at;
    const [x2, y2] = LAYOUT[to].at;
    if (to === "collector") return `M ${x1} ${y1 + NODE.h / 2} L ${TAP_X[from]} ${y2 - 26}`;
    const direct = SESSIONS.some(([a, b]) => (a === from && b === to) || (a === to && b === from));
    if (direct) return `M ${x1} ${y1} L ${x2} ${y2}`;
    return `M ${x1} ${y1 + NODE.h / 2} Q ${(x1 + x2) / 2} ${y1 + 150} ${x2} ${y2 + NODE.h / 2}`;
  }

  function pulse(role) {
    const node = layer("nodes").querySelector(`.node[data-role="${role}"]`);
    if (!node) return;
    node.classList.add("hot");
    setTimeout(() => node.classList.remove("hot"), 700);
  }

  // Moves one token from a router to another along the edge between them.
  function travel(from, to, label, kind) {
    return new Promise((resolve) => {
      const d = edgePath(from, to);
      const track = document.createElementNS("http://www.w3.org/2000/svg", "path");
      track.setAttribute("d", d);
      track.setAttribute("class", `track ${kind}`);
      layer("paths").appendChild(track);
      const length = track.getTotalLength();
      const token = document.createElementNS("http://www.w3.org/2000/svg", "g");
      token.setAttribute("class", `token ${kind}`);
      const width = Math.max(64, label.length * 9 + 18);
      token.innerHTML = `<rect x="${-width / 2}" y="-13" width="${width}" height="26" rx="13"></rect><text y="5" text-anchor="middle">${esc(label)}</text>`;
      layer("tokens").appendChild(token);
      const place = (t) => {
        const p = track.getPointAtLength(length * t);
        token.setAttribute("transform", `translate(${p.x} ${p.y})`);
      };
      const done = () => {
        pulse(to);
        setTimeout(() => { token.remove(); track.remove(); resolve(); }, reduced ? 500 : 120);
      };
      if (reduced) { place(1); done(); return; }
      const start = performance.now();
      const frame = (now) => {
        const t = Math.min(1, (now - start) / HOP_MS);
        place(t);
        if (t < 1) requestAnimationFrame(frame); else done();
      };
      place(0);
      requestAnimationFrame(frame);
    });
  }

  // A token that follows several hops in a row.
  async function route(hops, label, kind) {
    for (const [from, to] of hops) await travel(from, to, label, kind);
  }

  return { render, travel, route, pulse };
}
