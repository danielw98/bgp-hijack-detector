// Figure 5: what the Internet says today about an address or prefix, via RIPEstat.
// The answer leads with the RPKI verdict, then the facts behind it.

import { get } from "./api.js";
import { esc, shortName } from "./format.js";

const $ = (id) => document.getElementById(id);
const SEVERITY = ["invalid", "unknown", "not-found", "valid"];
const BADGE = { valid: "RPKI valid", invalid: "RPKI invalid", "not-found": "fără RPKI", unknown: "RPKI necunoscut" };

export function startLookup() {
  $("lookup-form").addEventListener("submit", (e) => { e.preventDefault(); lookup($("lookup-input").value); });
  $("btn-self").addEventListener("click", () => run("/api/lookup/self"));
  $("lookup-examples").addEventListener("click", (e) => {
    const b = e.target.closest("button[data-q]");
    if (b) { $("lookup-input").value = b.dataset.q; lookup(b.dataset.q); }
  });
}

function lookup(resource) {
  run(`/api/lookup?resource=${encodeURIComponent(resource.trim())}`);
}

async function run(path) {
  $("lookup-msg").textContent = "se întreabă RIPEstat...";
  try {
    const data = await get(path);
    if (data.ip) $("lookup-input").value = data.ip;
    render(data);
    $("raw-lookup").textContent = JSON.stringify(data, null, 2);
    $("lookup-msg").textContent = data.via === "server" ? "Adresa publică a calculatorului care servește pagina." : "";
  } catch (e) {
    $("lookup-result").hidden = true;
    $("lookup-msg").textContent = "Eroare: " + e.message;
  }
}

// The worst RPKI state among the origins decides the verdict.
function verdictOf(origins) {
  return SEVERITY.find((s) => origins.some((o) => o.rpki.status === s)) || "unknown";
}

function who(o) {
  const name = shortName(o.holder);
  return `AS${esc(o.asn)}${name ? ` (${esc(name)})` : ""}`;
}

function sentence(data, status) {
  const by = data.origins.map(who).join(" și ");
  const prefix = `<span class="mono">${esc(data.prefix)}</span>`;
  if (status === "valid") return `${prefix} este anunțat de ${by}, iar originea are autorizație.`;
  if (status === "invalid") return `${prefix} este anunțat de ${by}, dar autorizațiile publicate nu permit acest anunț.`;
  if (status === "not-found") return `${prefix} este anunțat de ${by}; deținătorul nu a publicat nicio autorizație RPKI.`;
  return `${prefix} este anunțat de ${by}; RIPEstat nu a putut evalua RPKI.`;
}

function meaning(data, status) {
  const asns = data.origins.map((o) => `AS${esc(o.asn)}`).join(" și ");
  if (status === "valid") return `deținătorul prefixului a publicat în RPKI că ${asns} are voie să-l anunțe. Un anunț al aceluiași prefix de la alt AS ar fi RPKI-invalid, iar rețelele care filtrează l-ar respinge.`;
  if (status === "invalid") return "anunțul contrazice autorizațiile publicate de deținător: originea sau lungimea prefixului nu se potrivește. Rețelele care validează RPKI îl resping; este semnul tipic al unei deturnări sau al unei erori de configurare.";
  if (status === "not-found") return "fără autorizație, RPKI nu poate spune nimic: un anunț al aceluiași prefix de la alt AS ar fi acceptat la fel de ușor. Așa erau toate prefixele în 2008, înainte de RPKI.";
  return "RIPEstat nu a întors o stare RPKI pentru acest prefix; încearcă din nou mai târziu.";
}

// RIPEstat rates each ROA against the announced origin and length.
const ROA_FIT = { valid: "autorizează anunțul", invalid_asn: "autorizează alt AS", invalid_length: "anunțul e mai lung decât permite" };

function roaText(r) {
  return `<span class="mono">${esc(r.prefix)}</span>, cel mult /${esc(r.max_length)}, pentru AS${esc(r.origin)}: ${esc(ROA_FIT[r.validity] || r.validity)}`;
}

function fact(label, value) {
  return `<div class="fact"><span class="k">${label}</span><span class="v">${value}</span></div>`;
}

function render(data) {
  const out = $("lookup-result");
  out.hidden = false;
  if (!data.prefix || !data.origins.length) {
    out.className = "answer";
    out.innerHTML = `<div class="verdict"><span class="label"><span class="mono">${esc(data.resource)}</span> nu este anunțat în BGP acum: nicio rută publică nu îl acoperă.</span></div>`;
    return;
  }
  const status = verdictOf(data.origins);
  const covering = data.resource === data.prefix ? "" : `, acoperă <span class="mono">${esc(data.resource)}</span>`;
  const roas = data.origins.flatMap((o) => o.rpki.roas);
  out.className = `answer ${status}`;
  out.innerHTML = `
    <div class="verdict"><span class="badge">${BADGE[status]}</span><span class="label">${sentence(data, status)}</span></div>
    <div class="facts">
      ${fact("Prefix", `<span class="mono">${esc(data.prefix)}</span>, ${data.announced ? "anunțat în BGP acum" : "neanunțat"}${covering}`)}
      ${fact("Origine", data.origins.map((o) => `<span class="mono">AS${esc(o.asn)}</span>`).join(", "))}
      ${fact("Deținător", data.origins.map((o) => esc(o.holder || "necunoscut")).join("; "))}
      ${fact("Autorizații (ROA)", roas.length ? roas.map(roaText).join("<br>") : "niciuna publicată")}
    </div>
    <p class="meaning"><b>Ce înseamnă:</b> ${meaning(data, status)}</p>`;
}
