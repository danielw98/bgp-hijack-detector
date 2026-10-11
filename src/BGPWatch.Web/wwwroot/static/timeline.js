// Figure 3: the recorded 2008 incident on one screen. One row per RIS peer: its lane,
// coloured by who its most specific route for the hijacked block pointed to, and its AS
// path at the cursor; the documented event under the cursor above the rows.

import { get, post } from "./api.js";
import { esc, fmtUtc, registerNames } from "./format.js";
import { contains, prefixLength } from "./simulate.js";

const $ = (id) => document.getElementById(id);
const INCIDENT = "youtube-2008";
const HIJACKER = 17557;
const OWNER = 36561;
const ORIGIN = {
  [HIJACKER]: { role: "attacker", lane: "red", name: "Pakistan Telecom" },
  [OWNER]: { role: "victim", lane: "green", name: "YouTube" },
};
const HIJACKED = "208.65.153.0/24";
const PROBE = "208.65.153.1";
const T0 = Date.UTC(2008, 1, 24, 18, 45);
const T1 = Date.UTC(2008, 1, 24, 21, 5);
const START = Date.UTC(2008, 1, 24, 18, 49);
const EVENT_LEAD_MS = 3 * 60 * 1000;
const TICK_EVERY_MIN = 30;
const SPEED = 180;
const TICK_MS = 100;
const SCRUB_STEP_S = 10;

export const incident = { meta: null, updates: [], peers: [] };
let timer = null;
let current = START;

export function startIncident2008() {
  $("btn-reload").addEventListener("click", reload);
  $("btn-play").addEventListener("click", togglePlay);
  $("inc-event").addEventListener("click", (e) => {
    const target = e.target.closest("[data-jump]");
    if (target) { stop(); setTime(Number(target.dataset.jump)); }
  });
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
    renderFirstSeen(incident.updates);
    renderRows();
    setTime(current);
    $("raw-2008").textContent = JSON.stringify({ meta, updates }, null, 2);
    $("msg-2008").textContent = updates.length
      ? `${updates.length} actualizări ale prefixelor YouTube, de la ${incident.peers.length} vecini ai colectorului rrc00.`
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
    if (!covering.length) return { peer, origin: OWNER, path: null };
    const best = covering.reduce((a, b) => (prefixLength(b.prefix) > prefixLength(a.prefix) ? b : a));
    return { peer, origin: best.origin_as, path: best.as_path };
  });
}

function laneOf(origin) {
  return (ORIGIN[origin] || { lane: "grey" }).lane;
}

// Position on the lane, 0..1000 in the lane's viewBox; pct() for HTML overlays.
function x(t) {
  return ((t - T0) / (T1 - T0)) * 1000;
}

function pct(t) {
  return `${x(t) / 10}%`;
}

function eventTimes() {
  return (incident.meta.events || []).map((ev) => Date.parse(ev.at));
}

function segmentsOf(peer) {
  const segments = [];
  let from = T0;
  let color = "green";
  incident.updates.filter((u) => u.peer === peer).forEach((u) => {
    const next = laneOf(stateAt(u.t).find((s) => s.peer === peer).origin);
    if (next !== color) { segments.push([from, u.t, color]); from = u.t; color = next; }
  });
  segments.push([from, T1, color]);
  return segments;
}

function peerAsn(peer) {
  const first = incident.updates.find((u) => u.peer === peer && u.as_path && u.as_path.length);
  return first ? `AS${first.as_path[0]}` : "";
}

function renderRows() {
  const events = eventTimes();
  const marks = events.map((t, i) => `<span class="mark" data-i="${i}" style="left:${pct(t)}">${i + 1}</span>`).join("");
  const eventLines = events.map((t, i) => `<line class="event" data-i="${i}" x1="${x(t)}" x2="${x(t)}" y1="0" y2="10" vector-effect="non-scaling-stroke"></line>`).join("");
  const rows = incident.peers.map((peer) => {
    const rects = segmentsOf(peer).map(([a, b, c]) => `<rect class="seg ${c}" x="${x(a)}" y="2" width="${Math.max(1, x(b) - x(a))}" height="6"></rect>`).join("");
    return `<div class="inc-row" data-peer="${esc(peer)}">
      <span class="c-peer"><span class="ip">${esc(peer)}</span> <span class="asn">${peerAsn(peer)}</span></span>
      <span class="c-lane"><svg viewBox="0 0 1000 10" preserveAspectRatio="none" aria-hidden="true">${rects}${eventLines}<line class="cursor" x1="0" x2="0" y1="0" y2="10" vector-effect="non-scaling-stroke"></line></svg></span>
      <span class="c-path"></span>
    </div>`;
  }).join("");
  const ticks = [];
  for (let t = T0; t <= T1; t += TICK_EVERY_MIN * 60 * 1000) ticks.push(`<span class="tick" style="left:${pct(t)}">${fmtUtc(new Date(t).toISOString(), false)}</span>`);
  ticks.push(`<span class="tick end" style="left:100%">${fmtUtc(new Date(T1).toISOString(), false)}</span>`);
  const max = (T1 - T0) / 1000;
  $("lanes").innerHTML = `
    <div class="inc-row head">
      <span class="c-peer">Vecin RIS</span>
      <span class="c-lane"><span class="track">${marks}<span class="handle"></span></span></span>
      <span class="c-path" id="path-head"></span>
    </div>
    ${rows}
    <div class="inc-row axis">
      <span class="c-peer"></span>
      <span class="c-lane"><span class="track">${ticks.join("")}</span><input type="range" id="scrub" min="0" max="${max}" step="${SCRUB_STEP_S}" aria-label="momentul din incident"></span>
      <span class="c-path"></span>
    </div>`;
  $("scrub").addEventListener("input", () => { stop(); setTime(T0 + Number($("scrub").value) * 1000); });
  $("lanes").querySelectorAll(".inc-row:not(.axis) .c-lane").forEach((lane) => lane.addEventListener("pointerdown", scrubFrom));
}

// Press and drag on any lane moves the cursor there.
function scrubFrom(down) {
  const box = down.currentTarget.getBoundingClientRect();
  const move = (e) => setTime(T0 + ((e.clientX - box.left) / box.width) * (T1 - T0));
  const up = () => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); };
  stop();
  move(down);
  window.addEventListener("pointermove", move);
  window.addEventListener("pointerup", up);
}

// Consecutive repeats (prepending) fold into one chip with a count.
function pathMarkup(path, origin) {
  if (!path) return `<span class="assumed">ruta /22 a YouTube, anunțată înainte</span>`;
  const folded = [];
  path.forEach((asn) => {
    const last = folded[folded.length - 1];
    if (last && last.asn === asn) last.times++;
    else folded.push({ asn, times: 1 });
  });
  const role = (ORIGIN[origin] || { role: "" }).role;
  const chips = folded.map(({ asn, times }, i) => {
    const kind = i === folded.length - 1 ? `origin ${role}` : i === folded.length - 2 ? `hand ${role}` : "";
    const repeat = times > 1 ? `<span class="times">&times;${times}</span>` : "";
    return `<span class="as ${kind}">${esc(asn)}${repeat}</span>`;
  });
  return `<span class="chain">${chips.join('<span class="sep">&rsaquo;</span>')}</span>`;
}

function setTime(t) {
  current = Math.max(T0, Math.min(T1, t));
  const state = stateAt(current);
  const clock = fmtUtc(new Date(current).toISOString());
  $("scrub").value = Math.round((current - T0) / 1000);
  $("scrub-time").textContent = `${clock} UTC`;
  $("path-head").textContent = `Calea AS la ${clock.slice(0, 5)}, spre origine`;
  $("lanes").querySelector(".handle").style.left = pct(current);
  $("lanes").querySelectorAll("line.cursor").forEach((line) => { line.setAttribute("x1", x(current)); line.setAttribute("x2", x(current)); });
  state.forEach((s) => {
    const row = $("lanes").querySelector(`.inc-row[data-peer="${s.peer}"]`);
    row.querySelector(".c-path").innerHTML = pathMarkup(s.path, s.origin);
  });
  renderCount("count-hijacker", state, HIJACKER);
  renderCount("count-owner", state, OWNER);
  renderCurrentEvent();
}

function renderCount(id, state, origin) {
  const n = state.filter((s) => s.origin === origin).length;
  $(id).innerHTML = `<span class="swatch"></span><b>${n} ${n === 1 ? "vecin" : "vecini"}</b><span class="to">&rarr; AS${origin} ${esc(ORIGIN[origin].name)}</span>`;
}

function renderCurrentEvent() {
  const events = incident.meta.events || [];
  const times = eventTimes();
  const i = times.filter((t) => t <= current).length - 1;
  $("lanes").querySelectorAll("[data-i]").forEach((el) => el.classList.toggle("current", Number(el.dataset.i) === i));
  const jump = (k, label) => k >= 0 && k < events.length
    ? `<button class="btn outline small" data-jump="${times[k] + EVENT_LEAD_MS}">${label}</button>`
    : `<button class="btn outline small" disabled>${k < 0 ? "&lsaquo;" : "&rsaquo;"}</button>`;
  const body = i >= 0
    ? `<span class="badge">${i + 1}</span><span class="t">${fmtUtc(events[i].at, false)}</span><span class="label">${esc(events[i].label)}.</span>`
    : `<span class="label">Niciun eveniment documentat încă: toți vecinii au ruta /22 a YouTube, anunțată înainte de fereastra înregistrată.</span>`;
  $("inc-event").innerHTML = `${body}<span class="jumps">${jump(i - 1, `&lsaquo; ${i}`)}${jump(i + 1, `${i + 2} &rsaquo;`)}</span>`;
}

function togglePlay() {
  if (timer) { stop(); return; }
  if (current >= T1) current = T0;
  $("btn-play").textContent = "Pauză";
  timer = setInterval(() => {
    setTime(current + SPEED * TICK_MS);
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

function renderFirstSeen(updates) {
  const first = new Map();
  updates.forEach((u) => {
    if (u.prefix === HIJACKED && u.origin_as === HIJACKER && !first.has(u.peer)) first.set(u.peer, u);
  });
  const rows = [...first.values()].sort((a, b) => a.t - b.t).map((u) => `
    <tr>
      <td class="time">${fmtUtc(u.seen_at)}</td>
      <td class="mono">${esc(u.peer)}</td>
      <td>${pathMarkup(u.as_path, HIJACKER)}</td>
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
