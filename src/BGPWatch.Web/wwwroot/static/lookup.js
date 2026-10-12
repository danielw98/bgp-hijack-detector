// Figure 5: what the Internet says today about an address or prefix, via RIPEstat.
// The answer leads with the RPKI verdict, then the facts behind it.

import { get } from "./api.js";
import { esc, shortName } from "./format.js";
import { and, t } from "./i18n.js";

const $ = (id) => document.getElementById(id);
const SEVERITY = ["invalid", "unknown", "not-found", "valid"];
const BADGE = { valid: t("rpki.badge.valid"), invalid: t("rpki.badge.invalid"), "not-found": t("rpki.badge.not-found"), unknown: t("rpki.badge.unknown") };

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
  $("lookup-msg").textContent = t("lookup.busy");
  try {
    const data = await get(path);
    if (data.ip) $("lookup-input").value = data.ip;
    render(data);
    $("raw-lookup").textContent = JSON.stringify(data, null, 2);
    $("lookup-msg").textContent = data.via === "server" ? t("lookup.server") : "";
  } catch (e) {
    $("lookup-result").hidden = true;
    $("lookup-msg").textContent = t("error", { message: e.message });
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
  const by = data.origins.map(who).join(and());
  const prefix = `<span class="mono">${esc(data.prefix)}</span>`;
  return t(`rpki.sentence.${SEVERITY.includes(status) ? status : "unknown"}`, { prefix, by });
}

function meaning(data, status) {
  const asns = data.origins.map((o) => `AS${esc(o.asn)}`).join(and());
  return t(`rpki.meaning.${SEVERITY.includes(status) ? status : "unknown"}`, { asns });
}

// RIPEstat rates each ROA against the announced origin and length.
const ROA_FIT = { valid: t("roa.valid"), invalid_asn: t("roa.invalid_asn"), invalid_length: t("roa.invalid_length") };

function roaText(r) {
  return t("roa.line", { prefix: `<span class="mono">${esc(r.prefix)}</span>`, max: esc(r.max_length), asn: esc(r.origin), fit: esc(ROA_FIT[r.validity] || r.validity) });
}

function fact(label, value) {
  return `<div class="fact"><span class="k">${label}</span><span class="v">${value}</span></div>`;
}

function render(data) {
  const out = $("lookup-result");
  out.hidden = false;
  if (!data.prefix || !data.origins.length) {
    out.className = "answer";
    out.innerHTML = `<div class="verdict"><span class="label">${t("lookup.unannounced", { resource: `<span class="mono">${esc(data.resource)}</span>` })}</span></div>`;
    return;
  }
  const status = verdictOf(data.origins);
  const covering = data.resource === data.prefix ? "" : t("lookup.covers", { resource: `<span class="mono">${esc(data.resource)}</span>` });
  const roas = data.origins.flatMap((o) => o.rpki.roas);
  out.className = `answer ${status}`;
  out.innerHTML = `
    <div class="verdict"><span class="badge">${BADGE[status]}</span><span class="label">${sentence(data, status)}</span></div>
    <div class="facts">
      ${fact(t("col.prefix"), `<span class="mono">${esc(data.prefix)}</span>, ${data.announced ? t("lookup.announced") : t("lookup.not.announced")}${covering}`)}
      ${fact(t("col.origin"), data.origins.map((o) => `<span class="mono">AS${esc(o.asn)}</span>`).join(", "))}
      ${fact(t("lookup.holder"), data.origins.map((o) => esc(o.holder || t("lookup.holder.unknown"))).join("; "))}
      ${fact(t("lookup.roas"), roas.length ? roas.map(roaText).join("<br>") : t("lookup.roas.none"))}
    </div>
    <p class="meaning"><b>${t("lookup.meaning")}</b> ${meaning(data, status)}</p>`;
}
