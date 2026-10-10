// Figure 3: the recorded 2008 incident as a timeline to scrub. One lane per RIS peer,
// coloured by who its most specific route for the hijacked block pointed to; the AS
// graph painted with the paths in use at the chosen moment; the documented events.

import { get, post } from "./api.js";
import { createAsGraph } from "./asgraph.js";
import { chain, esc, fmtUtc, nameOf, plural, registerNames, shortName } from "./format.js";
import { contains, prefixLength } from "./simulate.js";

const $ = (id) => document.getElementById(id);
const INCIDENT = "youtube-2008";
const HIJACKER = 17557;
const OWNER = 36561;
const HIJACKED = "208.65.153.0/24";
const PROBE = "208.65.153.1";
const T0 = Date.UTC(2008, 1, 24, 18, 45);
const T1 = Date.UTC(2008, 1, 24, 21, 5);
const START = Date.UTC(2008, 1, 24, 18, 49);
const SPEED = 180;
const TICK_MS = 100;
const LANE = { left: 150, right: 990, top: 34, height: 22 };
const COLOR = { [HIJACKER]: "red", [OWNER]: "green" };

export const incident = { meta: null, updates: [], peers: [] };
let graph = null;
let timer = null;
let current = START;

export function startIncident2008() {
  $("btn-reload").addEventListener("click", reload);
  $("btn-play").addEventListener("click", togglePlay);
  $("scrub").addEventListener("input", () => { stop(); setTime(T0 + Number($("scrub").value) * 1000); });
  load();
}

async function load() {
  try {
    const [meta, updates] = await Promise.all([get(`/api/incidents/${INCIDENT}`), get(`/api/incidents/${INCIDENT}/updates`)]);
    incident.meta = meta;
    incident.updates = updates.map((u) => ({ ...u, t: Date.parse(u.seen_at) })).sort((a, b) => a.t - b.t);
    incident.peers = [...new Set(incident.updates.map((u) => u.peer))];
    registerNames(meta.as_names);
    renderEvents(meta.events);
    renderPeers(incident.updates);
    graph = createAsGraph($("asgraph"), incident.updates.map((u) => u.as_path).filter((p) => p.length), { [HIJACKER]: "hijacker", [OWNER]: "owner" });
    renderLanes();
    setTime(current);
    $("raw-2008").textContent = JSON.stringify({ meta, updates }, null, 2);
    $("msg-2008").textContent = updates.length
      ? `${plural(updates.length, "actualizare", "actualizări")} ale prefixelor YouTube, de la ${plural(incident.peers.length, "vecin", "vecini")} ai colectorului. Căile lor trec prin ${plural(graph.size.nodes, "sistem autonom", "sisteme autonome")}; harta le arată pe cele ${graph.size.shown} care decid drumul: vecinii, cele care predau ruta originii și originile.`
      : "Incidentul nu este încărcat încă; apasă „Reîncarcă incidentul”.";
  } catch (e) {
    $("msg-2008").textContent = "Eroare: " + e.message;
  }
}

// What each peer's most specific route for the hijacked block pointed to at time t.
// Before any update in the window, every peer held YouTube's /22, announced earlier.
function stateAt(t) {
  const held = new Map();
  for (const u of incident.updates) {
    if (u.t > t) break;
    if (!held.has(u.peer)) held.set(u.peer, new Map());
    held.get(u.peer).set(u.prefix, u);
  }
  return incident.peers.map((peer) => {
    const covering = [...(held.get(peer) || new Map()).values()].filter((u) => !u.withdrawn && contains(u.prefix, PROBE));
    if (!covering.length) return { peer, origin: OWNER, path: null, prefix: "208.65.152.0/22", assumed: true };
    const best = covering.reduce((a, b) => (prefixLength(b.prefix) > prefixLength(a.prefix) ? b : a));
    return { peer, origin: best.origin_as, path: best.as_path, prefix: best.prefix, assumed: false };
  });
}

function colorOf(origin) {
  return COLOR[origin] || "grey";
}

function x(t) {
  return LANE.left + ((t - T0) / (T1 - T0)) * (LANE.right - LANE.left);
}

function renderLanes() {
  const peers = incident.peers;
  const height = LANE.top + peers.length * LANE.height + 40;
  const lanes = peers.map((peer, i) => {
    const y = LANE.top + i * LANE.height;
    const segments = [];
    let from = T0;
    let color = "green";
    const mine = incident.updates.filter((u) => u.peer === peer);
    mine.forEach((u) => {
      const next = colorOf(stateAt(u.t).find((s) => s.peer === peer).origin);
      if (next !== color) { segments.push([from, u.t, color]); from = u.t; color = next; }
    });
    segments.push([from, T1, color]);
    const rects = segments.map(([a, b, c]) => `<rect class="seg ${c}" x="${x(a)}" y="${y + 3}" width="${Math.max(1, x(b) - x(a))}" height="${LANE.height - 6}"></rect>`).join("");
    return `<text class="peer" x="${LANE.left - 8}" y="${y + 15}" text-anchor="end">${esc(peer)}</text>${rects}`;
  }).join("");
  const ticks = [];
  for (let t = T0; t <= T1; t += 15 * 60 * 1000) {
    ticks.push(`<line class="tick" x1="${x(t)}" y1="${LANE.top - 4}" x2="${x(t)}" y2="${height - 36}"></line><text class="ticklabel" x="${x(t)}" y="${height - 22}" text-anchor="middle">${fmtUtc(new Date(t).toISOString(), false)}</text>`);
  }
  const events = (incident.meta.events || []).map((ev, i) => {
    const t = Date.parse(ev.at);
    return `<line class="event" x1="${x(t)}" y1="${LANE.top - 16}" x2="${x(t)}" y2="${height - 36}"></line><text class="eventlabel" x="${x(t) + 4}" y="${LANE.top - 20}">${i + 1}</text>`;
  }).join("");
  $("lanes").innerHTML = `<svg class="lanes" viewBox="0 0 1000 ${height}" role="img" aria-label="Pentru fiecare vecin al colectorului, cui îi trimitea traficul pentru blocul deturnat, de-a lungul timpului">
    ${ticks.join("")}${events}${lanes}
    <g class="playhead"><line x1="0" y1="${LANE.top - 10}" x2="0" y2="${height - 36}"></line><polygon points="-6,${LANE.top - 18} 6,${LANE.top - 18} 0,${LANE.top - 8}"></polygon></g>
  </svg>`;
}

function setTime(t) {
  current = Math.max(T0, Math.min(T1, t));
  const state = stateAt(current);
  $("scrub").value = Math.round((current - T0) / 1000);
  $("scrub-time").textContent = fmtUtc(new Date(current).toISOString()) + " UTC";
  const head = $("lanes").querySelector(".playhead");
  if (head) head.setAttribute("transform", `translate(${x(current)} 0)`);
  if (graph) graph.paint(state.map((s) => ({ path: s.path, color: colorOf(s.origin) })));
  const toHijacker = state.filter((s) => s.origin === HIJACKER).length;
  const toOwner = state.filter((s) => s.origin === OWNER).length;
  const events = (incident.meta.events || []).filter((ev) => Date.parse(ev.at) <= current);
  const last = events.length ? `Ultimul eveniment documentat, ${fmtUtc(events[events.length - 1].at, false)}: ${esc(events[events.length - 1].label)}.` : "Niciun eveniment documentat încă; vecinii au ruta /22 a YouTube, anunțată înainte de fereastra înregistrată.";
  $("scrub-caption").innerHTML = `La <b>${fmtUtc(new Date(current).toISOString())} UTC</b>, din ${state.length} vecini, <b class="attacker">${toHijacker}</b> trimit traficul pentru <span class="mono">${PROBE}</span> către AS${HIJACKER} (${esc(shortName(nameOf(HIJACKER)))}) și <b class="victim">${toOwner}</b> către AS${OWNER} (${esc(shortName(nameOf(OWNER)))}). ${last}`;
}

function togglePlay() {
  if (timer) { stop(); return; }
  if (current >= T1) current = T0;
  $("btn-play").textContent = "Pauză";
  timer = setInterval(() => {
    const before = stateAt(current);
    setTime(current + SPEED * TICK_MS);
    stateAt(current).forEach((s, i) => { if (graph && s.path && s.origin !== before[i].origin) graph.pulse(s.path[0]); });
    if (current >= T1) stop();
  }, TICK_MS);
}

function stop() {
  if (timer) clearInterval(timer);
  timer = null;
  $("btn-play").textContent = "Redă";
}

function renderEvents(events) {
  $("events").innerHTML = events.map((ev, i) => `<li><span class="t">${i + 1}. ${fmtUtc(ev.at, false)}</span><span>${esc(ev.label)}</span></li>`).join("");
}

function renderPeers(updates) {
  const first = new Map();
  updates.forEach((u) => {
    if (u.prefix === HIJACKED && u.origin_as === HIJACKER && !first.has(u.peer)) first.set(u.peer, u);
  });
  const rows = [...first.values()].sort((a, b) => a.t - b.t).map((u) => `
    <tr>
      <td class="time">${fmtUtc(u.seen_at)}</td>
      <td class="mono">${esc(u.peer)}</td>
      <td>${chain(u.as_path)}</td>
    </tr>`).join("");
  $("peers-2008").innerHTML = rows
    ? `<table class="data"><thead><tr><th>Prima observare (UTC)</th><th>Vecin RIS</th><th>Calea AS către AS17557</th></tr></thead><tbody>${rows}</tbody></table>`
    : '<div class="empty">Nicio observare a sub-prefixului deturnat.</div>';
}

async function reload() {
  const btn = $("btn-reload");
  btn.disabled = true;
  $("msg-2008").textContent = "se reîncarcă din fișiere...";
  try {
    const res = await post("/api/lab/replay");
    $("msg-2008").textContent = `Reîncărcat: ${res.updates} actualizări, ${res.alerts} alerte.`;
    await load();
  } catch (e) {
    $("msg-2008").textContent = "Eroare: " + e.message;
  } finally {
    btn.disabled = false;
  }
}
