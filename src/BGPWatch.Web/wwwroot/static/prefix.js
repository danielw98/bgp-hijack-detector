// Figure 4: the history of a monitored prefix. One group per router (vantage point), one
// row per prefix it saw: a lane over the chosen window, coloured by whether the route
// points to an expected origin, and the path it holds now.

import { every, get } from "./api.js";
import { ALERT_NAME, esc, fmtTime, nameOf, pathChain, peerMarkup, peerName, registerLab, roleOf } from "./format.js";
import { prefixLength } from "./simulate.js";

const $ = (id) => document.getElementById(id);
const ROLE_ORDER = ["victim", "transit", "attacker"];
const LIST_LIMIT = 50;
const MIN = 60 * 1000;
const TICK_STEPS_MIN = [1, 2, 5, 10, 15, 30, 60, 120, 240, 360, 720, 1440];
const TICKS_WANTED = 5;
const END_LABEL_GAP = 0.8; // of a tick step: no tick label crowds the end label
const MIN_RED_WIDTH = 6; // lane units of 1000: a hijack of seconds stays visible in a long window

let monitored = [];
let current = "";
let windowMs = 10 * MIN;
let data = null;

export function startPrefix() {
  $("prefix-choice").addEventListener("click", (e) => {
    const b = e.target.closest("button");
    if (b) { current = b.dataset.prefix; press($("prefix-choice"), b); refresh(); }
  });
  $("window-choice").addEventListener("click", (e) => {
    const b = e.target.closest("button");
    if (b) { windowMs = Number(b.dataset.window) * 1000; press($("window-choice"), b); render(); }
  });
  loadMonitored().then(() => every(5000, refresh));
}

function press(group, button) {
  group.querySelectorAll("button").forEach((b) => b.setAttribute("aria-pressed", String(b === button)));
}

async function loadMonitored() {
  try {
    const [rows, lab] = await Promise.all([get("/api/monitored"), get("/api/lab").catch(() => null)]);
    if (lab) registerLab(lab.routers, lab.collector);
    monitored = rows;
    current = monitored.length ? monitored[0].prefix : "";
    $("prefix-choice").innerHTML = monitored.map((m, i) => `<button type="button" data-prefix="${esc(m.prefix)}" aria-pressed="${i === 0}"><span class="mono">${esc(m.prefix)}</span> <span class="note">${esc(shortDescription(m.description))}</span></button>`).join("");
  } catch (e) {
    $("prefix-verdict").textContent = "Eroare: " + e.message;
  }
}

// "lab victim network" -> "laborator"; "YouTube, 2008 incident replay" -> "YouTube 2008".
function shortDescription(text) {
  if (/^lab\b/i.test(text || "")) return "laborator";
  if (/youtube/i.test(text || "")) return "YouTube 2008";
  return text || "";
}

async function refresh() {
  if (!current) return;
  try {
    data = await get(`/api/prefix/${current}`);
    render();
    $("raw-prefix").textContent = JSON.stringify(data, null, 2);
  } catch (e) {
    $("prefix-verdict").textContent = "Eroare: " + e.message;
  }
}

function render() {
  if (!data) return;
  const recorded = data.current.length > 0 && data.current.every((s) => s.source !== "lab");
  const changes = data.changes.map((c) => ({ ...c, t: Date.parse(c.seen_at) })).sort((a, b) => a.t - b.t);
  const end = recorded && changes.length ? changes[changes.length - 1].t : Date.now();
  const start = windowMs ? end - windowMs : (changes.length ? changes[0].t : end - 10 * MIN);
  const view = { recorded, changes, start, end, expected: data.monitored ? data.monitored.expected_origins : [] };
  renderVerdict(view);
  renderLanes(view);
  renderChanges(view);
  renderAlerts(view);
}

function legit(state, expected) {
  return state && !state.withdrawn && expected.includes(state.origin_as);
}

function colorOf(state, expected) {
  if (!state || state.withdrawn) return "none";
  return legit(state, expected) ? "green" : "red";
}

function renderVerdict(view) {
  const live = data.current.filter((s) => !s.withdrawn);
  const routers = [...new Set(live.map((s) => s.peer))];
  const wrong = live.filter((s) => !legit(s, view.expected));
  const wrongRouters = [...new Set(wrong.map((s) => s.peer))];
  const origins = view.expected.map((a) => `AS${a}`).join(", ");
  const inWindow = data.alerts.filter((a) => Date.parse(a.raised_at) >= view.start).length;
  const counts = `${inWindow} ${inWindow === 1 ? "alertă" : "alerte"} în fereastră · ${data.alerts.length} în total${data.truncated ? " (istoric trunchiat)" : ""}`;
  const noun = view.recorded ? { one: "vecin RIS", many: "vecini RIS", all: "toți cei" } : { one: "ruter", many: "rutere", all: "toate cele" };
  const box = $("prefix-verdict");
  if (!live.length) {
    box.className = "verdict";
    box.innerHTML = `<span class="label">Nicio rută observată pentru ${esc(data.prefix)}.</span><span class="counts">${counts}</span>`;
    return;
  }
  const hijacked = wrongRouters.length > 0;
  box.className = `verdict ${hijacked ? "hijacked" : "legit"}`;
  const text = hijacked
    ? `Acum ${wrongRouters.length} din ${routers.length} ${routers.length === 1 ? `${noun.one} are` : `${noun.many} au`} o rută cu altă origine decât ${origins}: ${wrong.map((s) => `${esc(peerName(s.peer))} (${esc(s.prefix)} de la AS${esc(s.origin_as)})`).join(", ")}.`
    : `Acum ${routers.length === 1 ? `singurul ${noun.one} trimite` : `${noun.all} ${routers.length} ${noun.many} trimit`} ${esc(data.prefix)} la ${origins}, originea așteptată.`;
  box.innerHTML = `<span class="badge">${hijacked ? "deturnat" : "legitim"}</span><span class="label">${text}</span><span class="counts">${counts}</span>`;
}

// Routers in story order (lab roles), then by address; prefixes from the widest.
function groups(view) {
  const keys = new Map();
  view.changes.forEach((c) => keys.set(`${c.peer} ${c.prefix}`, { peer: c.peer, prefix: c.prefix }));
  data.current.forEach((s) => keys.set(`${s.peer} ${s.prefix}`, { peer: s.peer, prefix: s.prefix }));
  const byPeer = new Map();
  [...keys.values()].forEach((k) => { if (!byPeer.has(k.peer)) byPeer.set(k.peer, []); byPeer.get(k.peer).push(k.prefix); });
  const rank = (peer) => { const i = ROLE_ORDER.indexOf(roleOf(asnOf(peer, view))); return i < 0 ? ROLE_ORDER.length : i; };
  return [...byPeer.entries()]
    .sort(([a], [b]) => rank(a) - rank(b) || a.localeCompare(b, "en", { numeric: true }))
    .map(([peer, prefixes]) => ({ peer, prefixes: prefixes.sort((a, b) => prefixLength(a) - prefixLength(b) || a.localeCompare(b, "en", { numeric: true })) }));
}

// A router's own AS: the first AS of any path it reported.
function asnOf(peer, view) {
  const states = [...data.current.filter((s) => s.peer === peer), ...view.changes.filter((c) => c.peer === peer).flatMap((c) => [c.after, c.before])];
  const seen = states.find((s) => s && s.as_path && s.as_path.length);
  return seen ? seen.as_path[0] : null;
}

function segmentsOf(peer, prefix, view) {
  const mine = view.changes.filter((c) => c.peer === peer && c.prefix === prefix);
  const before = mine.filter((c) => c.t <= view.start);
  let state = before.length ? before[before.length - 1].after : (mine.length ? mine[0].before : null);
  let from = view.start;
  const segments = [];
  mine.filter((c) => c.t > view.start).forEach((c) => {
    segments.push([from, c.t, colorOf(state, view.expected)]);
    from = c.t;
    state = c.after;
  });
  segments.push([from, view.end, colorOf(state, view.expected)]);
  return segments.filter(([a, b]) => b > a);
}

function x(t, view) {
  return ((t - view.start) / (view.end - view.start)) * 1000;
}

function pct(t, view) {
  return `${x(t, view) / 10}%`;
}

function nowCell(peer, prefix, view) {
  const s = data.current.find((c) => c.peer === peer && c.prefix === prefix);
  if (!s) return '<span class="assumed">nicio rută</span>';
  if (s.withdrawn) return `<span class="assumed">nicio rută (retrasă la ${esc(fmtTime(s.seen_at, view.recorded))})</span>`;
  const role = legit(s, view.expected) ? "victim" : "attacker";
  if (!s.as_path.length) return `${pathChain([s.origin_as], role)} <span class="assumed">(proprie)</span>`;
  return pathChain(s.as_path, role);
}

function tickStepMs(span) {
  const step = TICK_STEPS_MIN.find((m) => span / (m * MIN) <= TICKS_WANTED) || TICK_STEPS_MIN[TICK_STEPS_MIN.length - 1];
  return step * MIN;
}

function clock(t, view) {
  const iso = new Date(t).toISOString();
  if (view.recorded) return iso.slice(11, 16);
  return new Date(t).toLocaleTimeString("ro-RO", { hour: "2-digit", minute: "2-digit" });
}

function renderLanes(view) {
  const rows = groups(view).map(({ peer, prefixes }) => {
    const asn = asnOf(peer, view);
    const role = roleOf(asn);
    const who = role ? `<b class="${role}">${esc(peerName(peer))}</b>` : `<span>${esc(peer)}</span>`;
    const head = `<div class="lane-row group"><span class="c-peer">${who} <span class="asn">${asn ? `AS${esc(asn)}` : ""}</span></span><span class="c-lane"></span><span class="c-path">${role ? `<span class="addr">${esc(peer)}</span>` : `<span class="holder" title="${esc(nameOf(asn))}">${esc(shortHolder(asn))}</span>`}</span></div>`;
    const lines = prefixes.map((prefix) => {
      const rects = segmentsOf(peer, prefix, view).map(([a, b, c]) => c === "none"
        ? `<rect class="seg none" x="${x(a, view)}" y="4.6" width="${Math.max(1, x(b, view) - x(a, view))}" height="0.8"></rect>`
        : `<rect class="seg ${c}" x="${x(a, view)}" y="2" width="${Math.max(c === "red" ? MIN_RED_WIDTH : 1, x(b, view) - x(a, view))}" height="6"></rect>`).join("");
      const sub = prefix !== data.prefix ? ' <span class="note">sub-prefix</span>' : "";
      return `<div class="lane-row"><span class="c-peer indent">${esc(prefix)}${sub}</span><span class="c-lane"><svg viewBox="0 0 1000 10" preserveAspectRatio="none" aria-hidden="true">${rects}<line class="cursor" x1="1000" x2="1000" y1="0" y2="10" vector-effect="non-scaling-stroke"></line></svg></span><span class="c-path">${nowCell(peer, prefix, view)}</span></div>`;
    }).join("");
    return head + lines;
  }).join("");
  const step = tickStepMs(view.end - view.start);
  const ticks = [];
  for (let t = Math.ceil(view.start / step) * step; t < view.end - step * END_LABEL_GAP; t += step) ticks.push(`<span class="tick" style="left:${pct(t, view)}">${clock(t, view)}</span>`);
  ticks.push(`<span class="tick end" style="left:100%">${view.recorded ? clock(view.end, view) : "acum"}</span>`);
  const legend = `Banda: verde = spre ${view.expected.map((a) => `AS${a}`).join(", ")}, roșu = spre alt AS, linie = nicio rută`;
  const pathHead = view.recorded ? `Calea AS la ${clock(view.end, view)} UTC, spre origine` : "Calea AS acum, spre origine";
  $("prefix-lanes").innerHTML = rows
    ? `<div class="lane-row head"><span class="c-peer">${view.recorded ? "Vecin RIS" : "Ruter"}, prefix</span><span class="c-lane legend">${legend}</span><span class="c-path">${pathHead}</span></div>${rows}
       <div class="lane-row axis"><span class="c-peer"></span><span class="c-lane"><span class="track">${ticks.join("")}</span></span><span class="c-path"></span></div>`
    : '<div class="empty">Nicio rută observată pentru acest prefix.</div>';
}

function shortHolder(asn) {
  return (nameOf(asn) || "").replace(/^[A-Z0-9-]+ - /, "").split(",")[0].trim();
}

function stateText(s) {
  if (!s) return '<span class="assumed">prima observare</span>';
  if (s.withdrawn) return '<b class="attacker">retras</b>';
  return pathChain(s.as_path.length ? s.as_path : [s.origin_as], "");
}

function renderChanges(view) {
  const list = [...view.changes].reverse();
  $("prefix-changes-summary").textContent = `Schimbările (${list.length})`;
  const rows = list.slice(0, LIST_LIMIT).map((c) => `
    <tr class="${colorOf(c.after, view.expected) === "red" ? "bad" : ""}">
      <td class="time">${fmtTime(c.seen_at, view.recorded)}</td>
      <td>${peerMarkup(c.peer)}</td>
      <td class="mono">${esc(c.prefix)}</td>
      <td>${stateText(c.before)}</td>
      <td>${stateText(c.after)}</td>
    </tr>`).join("");
  const more = list.length > LIST_LIMIT ? `<p class="caption">Ultimele ${LIST_LIMIT} din ${list.length}; toate sunt în „Date brute”.</p>` : "";
  $("prefix-changes").innerHTML = rows
    ? `<table class="data"><thead><tr><th>Când</th><th>Ruter</th><th>Prefix</th><th>Înainte</th><th>După</th></tr></thead><tbody>${rows}</tbody></table>${more}`
    : '<div class="empty">Nicio schimbare.</div>';
}

function renderAlerts(view) {
  const list = data.alerts;
  $("prefix-alerts-summary").textContent = `Alertele (${list.length})`;
  const rows = list.slice(0, LIST_LIMIT).map((a) => `
    <tr>
      <td class="time">${fmtTime(a.raised_at, view.recorded)}</td>
      <td>${esc(ALERT_NAME[a.alert_type] || a.alert_type)}</td>
      <td class="mono">${esc(a.prefix)}</td>
      <td>${peerMarkup(a.peer)}</td>
      <td class="mono">AS${esc(a.observed_origin)}</td>
    </tr>`).join("");
  const more = list.length > LIST_LIMIT ? `<p class="caption">Ultimele ${LIST_LIMIT} din ${list.length}; toate sunt în „Date brute”.</p>` : "";
  $("prefix-alerts").innerHTML = rows
    ? `<table class="data"><thead><tr><th>Când</th><th>Tip</th><th>Prefix</th><th>Ruter</th><th>Origine văzută</th></tr></thead><tbody>${rows}</tbody></table>${more}`
    : '<div class="empty">Nicio alertă.</div>';
}
