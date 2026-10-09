// Figure 3: the recorded 2008 incident: its documented events and what each RIS peer
// saw first. The scrubber and the AS graph are added by the next phase.

import { get, post } from "./api.js";
import { chain, esc, fmtUtc, registerNames } from "./format.js";

const $ = (id) => document.getElementById(id);
const INCIDENT = "youtube-2008";
const HIJACKER = 17557;
const HIJACKED = "208.65.153.0/24";

export const incident = { meta: null, updates: [] };

export function startIncident2008() {
  $("btn-reload").addEventListener("click", reload);
  load();
}

async function load() {
  try {
    const [meta, updates] = await Promise.all([get(`/api/incidents/${INCIDENT}`), get(`/api/incidents/${INCIDENT}/updates`)]);
    incident.meta = meta;
    incident.updates = updates;
    registerNames(meta.as_names);
    renderEvents(meta.events);
    renderPeers(updates);
    $("raw-2008").textContent = JSON.stringify({ meta, updates }, null, 2);
    $("msg-2008").textContent = updates.length
      ? `${updates.length} actualizări ale prefixelor YouTube, din ${new Set(updates.map((u) => u.peer)).size} vecini ai colectorului.`
      : "Incidentul nu este încărcat încă; apasă „Reîncarcă incidentul”.";
  } catch (e) {
    $("msg-2008").textContent = "Eroare: " + e.message;
  }
}

function renderEvents(events) {
  $("events").innerHTML = events.map((ev) => `<li><span class="t">${fmtUtc(ev.at, false)}</span><span>${esc(ev.label)}</span></li>`).join("");
}

function renderPeers(updates) {
  const first = new Map();
  updates.forEach((u) => {
    if (u.prefix === HIJACKED && u.origin_as === HIJACKER && !first.has(u.peer)) first.set(u.peer, u);
  });
  const rows = [...first.values()].sort((a, b) => a.seen_at.localeCompare(b.seen_at)).map((u) => `
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
