// Figure 2: the alerts grouped into incidents, each with its plain-language explanation.

import { every, get } from "./api.js";
import { ALERT_NAME, asChip, chain, esc, fmtTime, nameOf, peerMarkup, peerName, plural, pretty } from "./format.js";
import { setData } from "./status.js";

const $ = (id) => document.getElementById(id);
const SOURCE_NAME = { lab: "laborator", "youtube-2008": "YouTube, 2008" };
const open = new Set();
let shown = [];

export function startIncidents() {
  $("incidents").addEventListener("click", (e) => {
    const row = e.target.closest(".incident");
    if (row && !e.target.closest("a")) toggle(row.dataset.key);
  });
  $("incidents").addEventListener("keydown", (e) => {
    const row = e.target.closest(".incident");
    if (row && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); toggle(row.dataset.key); }
  });
  every(3000, refresh);
}

async function refresh() {
  try {
    const [incidents, alerts] = await Promise.all([get("/api/incidents"), get("/api/alerts?limit=200")]);
    shown = incidents;
    render(incidents);
    $("raw-incidents").textContent = JSON.stringify({ incidents, alerts }, null, 2);
    setData(true);
  } catch (e) {
    setData(false);
  }
}

function keyOf(i) {
  return `${i.source}|${i.prefix}|${i.origin}|${i.first_seen}`;
}

function toggle(key) {
  if (!open.delete(key)) open.add(key);
  render(shown);
}

function render(incidents) {
  const box = $("incidents");
  if (!incidents.length) {
    box.innerHTML = '<div class="empty">Niciun incident. Apasă „Simulează deturnarea” în Figura 1.</div>';
    return;
  }
  box.innerHTML = incidents.map((i) => {
    const key = keyOf(i);
    const isOpen = open.has(key);
    const prefixes = i.prefixes.map((p) => `<span class="mono">${esc(p)}</span>`).join(" și ");
    const expected = i.expected_origins.map((a) => asChip(a)).join(", ");
    const owned = i.prefixes.length > 1 ? "deținute de" : "deținut de";
    const utc = i.source !== "lab";
    const when = i.first_seen === i.last_seen ? fmtTime(i.first_seen, utc) : `${fmtTime(i.first_seen, utc)} până la ${fmtTime(i.last_seen, utc)}`;
    return `
      <div class="incident ${isOpen ? "open" : ""}" data-key="${esc(key)}" tabindex="0" role="button" aria-expanded="${isOpen}">
        <div class="head">
          <span class="what"><span class="chain">${asChip(i.origin, "origin")}</span> anunță ${prefixes}${expected ? `, ${owned} <span class="chain">${expected}</span>` : ""}</span>
          <span class="meta">${i.alert_types.map((t) => ALERT_NAME[t]).join(", ")}</span>
          <span class="meta">${plural(i.peers.length, "punct de observație", "puncte de observație")}</span>
          <span class="meta">${when}</span>
          <span class="tag">${esc(SOURCE_NAME[i.source] || i.source)}</span>
        </div>
        ${isOpen ? `<div class="explain">${explain(i)}</div>` : ""}
      </div>`;
  }).join("");
}

function explain(i) {
  const origin = `<b>AS${i.origin}</b>${nameOf(i.origin) ? ` (${esc(nameOf(i.origin))})` : ""}`;
  const expected = i.expected_origins.length ? `<b>AS${i.expected_origins.join(", AS")}</b>` : "";
  const monitored = `<b class="mono">${esc(i.prefix)}</b>`;
  const parts = [];
  if (i.alert_types.includes("origin_hijack")) {
    parts.push(`${origin} anunță prefixul ${monitored} ca și cum i-ar aparține, dar originea legitimă cunoscută este ${expected}. Ruterele care aleg acest anunț trimit traficul către AS${i.origin}, nu către proprietar.`);
  }
  if (i.alert_types.includes("subprefix_hijack")) {
    const subs = i.prefixes.filter((p) => p !== i.prefix).map((p) => `<b class="mono">${esc(p)}</b>`).join(" și ");
    parts.push(`${origin} anunță ${subs || "un sub-prefix"}, o parte mai specifică din ${monitored}${expected ? `, a cărui origine legitimă este ${expected}` : ""}. Ruterele aleg mereu ruta cea mai specifică, așa că anunțul câștigă în fața rutei legitime oriunde ajunge.`);
  }
  if (i.alert_types.includes("rpki_invalid")) {
    parts.push(`Anunțurile contrazic RPKI: nicio autorizație de origine (ROA) nu îi permite lui AS${i.origin} să anunțe aceste prefixe la aceste lungimi. Un ruter care validează originea respinge, de regulă, astfel de rute.`);
  }
  const peers = i.peers.map((p) => peerMarkup(p)).join(", ");
  parts.push(`Colectorul a văzut anunțul din ${plural(i.peers.length, "punct de observație", "puncte de observație")}: ${peers}. ${i.alert_ids.length} alerte în total; rândurile lor sunt în „Date brute”.`);
  return parts.map((p) => `<p>${p}</p>`).join("");
}
