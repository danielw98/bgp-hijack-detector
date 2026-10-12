// Figure 2: the alerts grouped into incidents, each with its plain-language explanation.
// The guide shows them twice, in Figure 2 and in the simulator's inspector, so every
// feed and every raw drawer is found by its data attribute.

import { every, get } from "./api.js";
import { ALERT_NAME, asChip, chain, esc, fmtTime, nameOf, peerMarkup, peerName, pretty } from "./format.js";
import { and, count, t } from "./i18n.js";
import { setData } from "./status.js";

const $ = (id) => document.getElementById(id);
const all = (attr) => document.querySelectorAll(`[${attr}]`);
const SOURCE_NAME = { lab: t("source.lab"), "youtube-2008": "YouTube, 2008" };
const open = new Set();
let shown = [];

export function startIncidents() {
  all("data-incidents").forEach((box) => {
    box.addEventListener("click", (e) => {
      const row = e.target.closest(".incident");
      if (row && !e.target.closest("a")) toggle(row.dataset.key);
    });
    box.addEventListener("keydown", (e) => {
      const row = e.target.closest(".incident");
      if (row && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); toggle(row.dataset.key); }
    });
  });
  every(3000, refresh);
}

async function refresh() {
  try {
    const [incidents, alerts] = await Promise.all([get("/api/incidents"), get("/api/alerts?limit=200")]);
    shown = incidents;
    render(incidents);
    renderCount(incidents.filter((i) => i.source === "lab").length);
    const raw = JSON.stringify({ incidents, alerts }, null, 2);
    all("data-raw-incidents").forEach((pre) => { pre.textContent = raw; });
    setData(true);
  } catch (e) {
    setData(false);
  }
}

// The simulator's incidents on the inspector tab, red while there are any.
function renderCount(n) {
  const count = $("incident-count");
  if (!count) return;
  count.textContent = n || "";
  count.classList.toggle("alert", n > 0);
}

function keyOf(i) {
  return `${i.source}|${i.prefix}|${i.origin}|${i.first_seen}`;
}

function toggle(key) {
  if (!open.delete(key)) open.add(key);
  render(shown);
}

function render(incidents) {
  const html = feed(incidents);
  all("data-incidents").forEach((box) => { box.innerHTML = html; });
}

function feed(incidents) {
  if (!incidents.length) return `<div class="empty">${esc(t("incidents.none"))}</div>`;
  return incidents.map((i) => {
    const key = keyOf(i);
    const isOpen = open.has(key);
    const prefixes = i.prefixes.map((p) => `<span class="mono">${esc(p)}</span>`).join(and());
    const expected = i.expected_origins.map((a) => asChip(a)).join(", ");
    const owned = i.prefixes.length > 1 ? t("incident.owned.many") : t("incident.owned.one");
    const utc = i.source !== "lab";
    const when = i.first_seen === i.last_seen ? fmtTime(i.first_seen, utc) : t("incident.span", { from: fmtTime(i.first_seen, utc), to: fmtTime(i.last_seen, utc) });
    return `
      <div class="incident ${isOpen ? "open" : ""}" data-key="${esc(key)}" tabindex="0" role="button" aria-expanded="${isOpen}">
        <div class="head">
          <span class="what">${t("incident.what", { origin: `<span class="chain">${asChip(i.origin, "origin")}</span>`, prefixes })}${expected ? `, ${owned} <span class="chain">${expected}</span>` : ""}</span>
          <span class="meta">${i.alert_types.map((t) => ALERT_NAME[t]).join(", ")}</span>
          <span class="meta">${count(i.peers.length, "incident.vantages")}</span>
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
    parts.push(t("explain.origin", { origin, monitored, expected, asn: i.origin }));
  }
  if (i.alert_types.includes("subprefix_hijack")) {
    const subs = i.prefixes.filter((p) => p !== i.prefix).map((p) => `<b class="mono">${esc(p)}</b>`).join(and());
    parts.push(t("explain.subprefix", { origin, subs: subs || t("explain.subprefix.some"), monitored, owner: expected ? t("explain.subprefix.owner", { expected }) : "" }));
  }
  if (i.alert_types.includes("rpki_invalid")) {
    parts.push(t("explain.rpki", { asn: i.origin }));
  }
  const peers = i.peers.map((p) => peerMarkup(p)).join(", ");
  parts.push(t("explain.seen", { vantages: count(i.peers.length, "incident.vantages"), peers, alerts: count(i.alert_ids.length, "incident.alerts") }));
  return parts.map((p) => `<p>${p}</p>`).join("");
}
