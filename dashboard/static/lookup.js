// Figure 5: what the Internet says today about an address or prefix, via RIPEstat.

import { get } from "./api.js";
import { esc, plural } from "./format.js";

const $ = (id) => document.getElementById(id);
const RPKI_TEXT = { valid: "valid", invalid: "invalid", "not-found": "fără autorizație", unknown: "necunoscut" };

export function startLookup() {
  $("lookup-form").addEventListener("submit", (e) => { e.preventDefault(); run(`/api/lookup?resource=${encodeURIComponent($("lookup-input").value)}`); });
  $("btn-self").addEventListener("click", () => run("/api/lookup/self"));
}

async function run(path) {
  const out = $("lookup-result");
  $("lookup-msg").textContent = "se întreabă RIPEstat...";
  try {
    const data = await get(path);
    if (data.ip) $("lookup-input").value = data.ip;
    render(data);
    $("raw-lookup").textContent = JSON.stringify(data, null, 2);
    $("lookup-msg").textContent = data.via === "server" ? "Adresa publică a calculatorului care servește pagina." : "";
  } catch (e) {
    out.innerHTML = "";
    $("lookup-msg").textContent = "Eroare: " + e.message;
  }
}

function render(data) {
  const out = $("lookup-result");
  if (!data.prefix) {
    out.innerHTML = `<p><span class="mono">${esc(data.resource)}</span> nu este anunțat în BGP acum: nicio rută publică nu îl acoperă.</p>`;
    return;
  }
  const origins = data.origins.map((o) => `
    <p>Originat de <b>AS${o.asn}</b>${o.holder ? `, ${esc(o.holder)}` : ""}. RPKI: <span class="rpki ${esc(o.rpki.status)}">${esc(RPKI_TEXT[o.rpki.status] || o.rpki.status)}</span>
    ${o.rpki.roas.length ? `<br><span class="small">${plural(o.rpki.roas.length, "autorizație", "autorizații")}: ${o.rpki.roas.map((r) => `<span class="mono">${esc(r.prefix)}</span> până la /${esc(r.max_length)} pentru AS${esc(r.origin)} (${esc(r.validity)})`).join("; ")}</span>` : ""}</p>`).join("");
  out.innerHTML = `
    <p class="big">${esc(data.prefix)}</p>
    <p class="small">${data.resource === data.prefix ? "Prefixul" : `Prefixul care acoperă <span class="mono">${esc(data.resource)}</span>`}, ${data.announced ? "anunțat în BGP" : "neanunțat"}.</p>
    ${origins}`;
}
