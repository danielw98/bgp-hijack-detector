// Figure 3: the recorded 2008 incident on one screen. One row per RIS peer: its lane,
// coloured by who its most specific route for the hijacked block pointed to, and its AS
// path at the cursor; the documented event under the cursor above the rows. The lane's
// axis is cut into phases at the documented events: linear inside a phase, each phase as
// wide as the square root of its duration (at least MIN_PHASE of the lane), its real
// duration written above it, a break mark on a phase drawn much narrower than linear.

import { get, post } from "./api.js";
import { esc, fmtUtc, pathChain, registerNames } from "./format.js";
import { contains, prefixLength } from "./simulate.js";
import { count, t } from "./i18n.js";

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
const EVENT_LEAD_MS = 3 * 60 * 1000;
const MIN_PHASE = 0.1;
const MARK_RADIUS = 13;
const TICK_GAP = 6;
const BROKEN_BELOW = 2 / 3;
const PLAY_MS = 45000;
const TICK_MS = 100;
const SCRUB_STEPS = 1000;

export const incident = { meta: null, updates: [], peers: [] };
let timer = null;
let current = T0;
let phases = [];

export function startIncident2008() {
  $("btn-reload").addEventListener("click", reload);
  $("btn-play").addEventListener("click", togglePlay);
  $("event-card").addEventListener("click", (e) => {
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
      ? t("i2008.loaded", { updates: count(updates.length, "i2008.updates"), peers: count(incident.peers.length, "i2008.peers") })
      : t("i2008.empty");
  } catch (e) {
    $("msg-2008").textContent = t("error", { message: e.message });
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

// The phases between T0, the documented events and T1, each with its share of the lane.
function phasesOf(times) {
  const bounds = [T0, ...times.filter((t) => t > T0 && t < T1), T1];
  const spans = bounds.slice(1).map((b, i) => ({ from: bounds[i], to: b }));
  const roots = spans.map((s) => Math.sqrt(s.to - s.from));
  const total = roots.reduce((a, b) => a + b, 0);
  const floored = roots.map((r) => Math.max(MIN_PHASE, r / total));
  const sum = floored.reduce((a, b) => a + b, 0);
  let left = 0;
  return spans.map((s, i) => {
    const share = floored[i] / sum;
    const phase = { ...s, left, share, broken: share < BROKEN_BELOW * ((s.to - s.from) / (T1 - T0)) };
    left += share;
    return phase;
  });
}

function phaseAt(t) {
  return phases.find((p) => t <= p.to) || phases[phases.length - 1];
}

// Position on the lane, 0..1000 in the lane's viewBox; pct() for HTML overlays.
function x(t) {
  const p = phaseAt(t);
  return (p.left + p.share * ((t - p.from) / (p.to - p.from))) * 1000;
}

// The time at a position on the lane, 0..1.
function timeAt(f) {
  const p = phases.find((q) => f <= q.left + q.share) || phases[phases.length - 1];
  return p.from + ((f - p.left) / p.share) * (p.to - p.from);
}

// A duration sits centred in the room its phase leaves between the event badges.
function durShift(i) {
  const left = i > 0 ? MARK_RADIUS : 0;
  const right = i < phases.length - 1 ? MARK_RADIUS : 0;
  return (left - right) / 2;
}

function duration(ms) {
  const min = Math.round(ms / 60000);
  return min < 60 ? `${min} min` : `${Math.floor(min / 60)} h ${min % 60} min`;
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
  phases = phasesOf(events);
  const marks = events.map((t, i) => `<span class="mark" data-i="${i}" style="left:${pct(t)}">${i + 1}</span>`).join("") +
    phases.map((p, i) => `<span class="dur" style="left:calc(${(p.left + p.share / 2) * 100}% + ${durShift(i)}px)">${p.broken ? "// " : ""}${duration(p.to - p.from)}</span>`).join("");
  const breaks = phases.filter((p) => p.broken)
    .map((p) => `<span class="break" style="left:calc(16px + (100% - 32px) * ${p.left + p.share / 2})"></span>`).join("");
  const eventLines = events.map((t, i) => `<line class="event" data-i="${i}" x1="${x(t)}" x2="${x(t)}" y1="0" y2="10" vector-effect="non-scaling-stroke"></line>`).join("");
  const rows = incident.peers.map((peer) => {
    const rects = segmentsOf(peer).map(([a, b, c]) => `<rect class="seg ${c}" x="${x(a)}" y="2" width="${Math.max(1, x(b) - x(a))}" height="6"></rect>`).join("");
    return `<div class="lane-row" data-peer="${esc(peer)}">
      <span class="c-peer"><span class="ip">${esc(peer)}</span> <span class="asn">${peerAsn(peer)}</span></span>
      <span class="c-lane"><svg viewBox="0 0 1000 10" preserveAspectRatio="none" aria-hidden="true">${rects}${eventLines}<line class="cursor" x1="0" x2="0" y1="0" y2="10" vector-effect="non-scaling-stroke"></line></svg>${breaks}</span>
      <span class="c-path"></span>
    </div>`;
  }).join("");
  const ticks = events.map((t) => `<span class="tick" style="left:${pct(t)}">${fmtUtc(new Date(t).toISOString(), false)}</span>`);
  $("lanes").innerHTML = `
    <div class="lane-row head">
      <span class="c-peer">${t("col.peer")}</span>
      <span class="c-lane"><span class="track">${marks}<span class="handle"></span></span></span>
      <span class="c-path" id="path-head"></span>
    </div>
    ${rows}
    <div class="lane-row axis">
      <span class="c-peer"></span>
      <span class="c-lane"><span class="track">${ticks.join("")}</span><input type="range" id="scrub" min="0" max="${SCRUB_STEPS}" step="1" aria-label="${esc(t("i2008.scrub"))}"></span>
      <span class="c-path"></span>
    </div>`;
  $("scrub").addEventListener("input", () => { stop(); setTime(timeAt(Number($("scrub").value) / SCRUB_STEPS)); });
  new ResizeObserver(hideCrowdedTicks).observe($("lanes"));
  $("lanes").querySelectorAll(".lane-row:not(.axis) .c-lane").forEach((lane) => lane.addEventListener("pointerdown", scrubFrom));
}

// On a narrow lane two event times can meet: the later one is hidden.
function hideCrowdedTicks() {
  let right = -Infinity;
  $("lanes").querySelectorAll(".lane-row.axis .tick").forEach((tick) => {
    tick.style.visibility = "";
    const box = tick.getBoundingClientRect();
    if (box.left < right + TICK_GAP) tick.style.visibility = "hidden";
    else right = box.right;
  });
}

// Press and drag on any lane moves the cursor there.
function scrubFrom(down) {
  const box = down.currentTarget.getBoundingClientRect();
  const move = (e) => setTime(timeAt(Math.max(0, Math.min(1, (e.clientX - box.left) / box.width))));
  const up = () => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); };
  stop();
  move(down);
  window.addEventListener("pointermove", move);
  window.addEventListener("pointerup", up);
}

function pathMarkup(path, origin) {
  if (!path) return `<span class="assumed">${t("i2008.assumed")}</span>`;
  return pathChain(path, (ORIGIN[origin] || { role: "" }).role);
}

function setTime(time) {
  current = Math.max(T0, Math.min(T1, time));
  const state = stateAt(current);
  const clock = fmtUtc(new Date(current).toISOString());
  $("scrub").value = Math.round((x(current) / 1000) * SCRUB_STEPS);
  $("speed-2008").textContent = t("i2008.pace");
  $("scrub-time").textContent = `${clock} UTC`;
  $("path-head").textContent = t("i2008.path.head", { time: clock.slice(0, 5) });
  $("lanes").querySelector(".handle").style.left = pct(current);
  $("lanes").querySelectorAll("line.cursor").forEach((line) => { line.setAttribute("x1", x(current)); line.setAttribute("x2", x(current)); });
  state.forEach((s) => {
    const row = $("lanes").querySelector(`.lane-row[data-peer="${s.peer}"]`);
    row.querySelector(".c-path").innerHTML = pathMarkup(s.path, s.origin);
  });
  renderCount("count-hijacker", state, HIJACKER);
  renderCount("count-owner", state, OWNER);
  renderCurrentEvent();
}

function renderCount(id, state, origin) {
  const n = state.filter((s) => s.origin === origin).length;
  $(id).innerHTML = `<span class="swatch"></span><b>${count(n, "i2008.peers.short")}</b><span class="to">&rarr; AS${origin} ${esc(ORIGIN[origin].name)}</span>`;
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
    : `<span class="label">${t("i2008.before")}</span>`;
  $("event-card").innerHTML = `${body}<span class="jumps">${jump(i - 1, `&lsaquo; ${i}`)}${jump(i + 1, `${i + 2} &rsaquo;`)}</span>`;
}

// Playback moves the cursor at one speed along the lane (PLAY_MS for the whole window),
// so the incident's clock runs faster through a compressed phase.
function togglePlay() {
  if (timer) { stop(); return; }
  if (current >= T1) current = T0;
  $("btn-play").textContent = t("pause");
  timer = setInterval(() => {
    setTime(timeAt(Math.min(1, x(current) / 1000 + TICK_MS / PLAY_MS)));
    if (current >= T1) stop();
  }, TICK_MS);
}

function stop() {
  if (timer) clearInterval(timer);
  timer = null;
  $("btn-play").textContent = t("play");
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
    ? `<table class="data"><thead><tr><th>${t("i2008.first.when")}</th><th>${t("col.peer")}</th><th>${t("i2008.first.path")}</th></tr></thead><tbody>${rows}</tbody></table>`
    : `<div class="empty">${t("i2008.first.none")}</div>`;
}

async function reload() {
  const btn = $("btn-reload");
  btn.disabled = true;
  $("msg-2008").textContent = t("i2008.reloading");
  try {
    const res = await post("/api/lab/replay");
    $("msg-2008").textContent = t("i2008.reloaded", { updates: res.updates, alerts: res.alerts });
    await load();
  } catch (e) {
    $("msg-2008").textContent = t("error", { message: e.message });
  } finally {
    btn.disabled = false;
  }
}
