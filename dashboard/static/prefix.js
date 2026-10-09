// Figure 4: the history of a monitored prefix: who announces it now at each vantage
// point, and every change the collector saw.

import { every, get } from "./api.js";
import { asChip, chain, esc, fmtTime, peerMarkup, plural } from "./format.js";

const $ = (id) => document.getElementById(id);
let current = "";

export function startPrefix() {
  $("prefix-select").addEventListener("change", () => { current = $("prefix-select").value; refresh(); });
  loadMonitored().then(() => every(5000, refresh));
}

async function loadMonitored() {
  try {
    const rows = await get("/api/monitored");
    $("prefix-select").innerHTML = rows.map((m) => `<option value="${esc(m.prefix)}">${esc(m.prefix)}, ${esc(m.description || "")}</option>`).join("");
    current = rows.length ? rows[0].prefix : "";
  } catch (e) {
    $("prefix-head").textContent = "Eroare: " + e.message;
  }
}

async function refresh() {
  if (!current) return;
  try {
    const data = await get(`/api/prefix/${current}`);
    const utc = data.current.some((s) => s.source !== "lab");
    const expected = data.monitored ? data.monitored.expected_origins : [];
    renderHead(data);
    renderCurrent(data.current, utc);
    renderChanges(data.changes, utc, expected);
    $("raw-prefix").textContent = JSON.stringify(data, null, 2);
  } catch (e) {
    $("prefix-head").textContent = "Eroare: " + e.message;
  }
}

function renderHead(data) {
  const m = data.monitored;
  const expected = m ? m.expected_origins.map((a) => asChip(a, "origin")).join(" ") : "";
  const live = data.current.filter((s) => !s.withdrawn);
  const wrong = live.filter((s) => m && !m.expected_origins.includes(s.origin_as));
  const now = live.length
    ? (wrong.length
      ? `Acum, <b class="attacker">${plural(wrong.length, "punct de observație vede", "puncte de observație văd")}</b> o altă origine pentru prefix sau pentru o parte din el.`
      : `Acum, toate cele ${live.length} rute observate pornesc de la originea legitimă.`)
    : "Nicio rută observată încă.";
  $("prefix-head").innerHTML = `
    <p class="big mono">${esc(data.prefix)}</p>
    <p>${m ? `Originat legitim de <span class="chain">${expected}</span>${m.description ? `, ${esc(m.description)}` : ""}.` : "Prefix neurmărit."} ${now} ${plural(data.alerts.length, "alertă", "alerte")} în istoric.</p>`;
}

function renderCurrent(states, utc) {
  const rows = states.map((s) => `
    <tr>
      <td>${peerMarkup(s.peer)}</td>
      <td class="mono">${esc(s.prefix)}${s.withdrawn ? ' <b class="attacker">(retras)</b>' : ""}</td>
      <td>${s.withdrawn ? "" : chain(s.as_path, s.origin_as)}</td>
      <td class="time">${fmtTime(s.seen_at, utc)}</td>
    </tr>`).join("");
  $("prefix-current").innerHTML = rows
    ? `<table class="data"><thead><tr><th>Punct de observație</th><th>Prefix</th><th>Calea AS acum</th><th>De la</th></tr></thead><tbody>${rows}</tbody></table>`
    : '<div class="empty">Nicio rută observată.</div>';
}

// A change is tinted by where the route points afterwards: red for an origin that is
// not legitimate, green when a legitimate origin replaces one that was not.
function tint(c, expected) {
  if (c.after.withdrawn || !expected.length) return "";
  const legit = (s) => s && !s.withdrawn && expected.includes(s.origin_as);
  if (!legit(c.after)) return "bad";
  return c.before && !legit(c.before) && !c.before.withdrawn ? "good" : "";
}

function renderChanges(changes, utc, expected) {
  const rows = changes.slice(0, 25).map((c) => `
    <tr class="${tint(c, expected)}">
      <td class="time">${fmtTime(c.seen_at, utc)}</td>
      <td>${peerMarkup(c.peer)}</td>
      <td class="mono">${esc(c.prefix)}</td>
      <td>${state(c.before)}</td>
      <td>${state(c.after)}</td>
    </tr>`).join("");
  const more = changes.length > 25 ? `<p class="caption" style="padding: 10px 14px">Ultimele 25 din ${changes.length} schimbări; toate sunt în „Date brute”.</p>` : "";
  $("prefix-changes").innerHTML = rows
    ? `<table class="data"><thead><tr><th>Când</th><th>Punct de observație</th><th>Prefix</th><th>Înainte</th><th>După</th></tr></thead><tbody>${rows}</tbody></table>${more}`
    : '<div class="empty">Nicio schimbare.</div>';
}

function state(s) {
  if (!s) return '<span class="msg">prima observare</span>';
  if (s.withdrawn) return '<b class="attacker">retras</b>';
  return chain(s.as_path, s.origin_as);
}
