// Text and markup shared by the figures: escaping, times, AS names and the path chain.

export const ROLE_NAME = { victim: "Victimă", transit: "Tranzit", attacker: "Atacator", collector: "Colector" };
export const ALERT_NAME = { origin_hijack: "origine falsă", subprefix_hijack: "sub-prefix", rpki_invalid: "RPKI-invalid" };
export const REASON = { "Older Path": "ruta mai veche", "Weight": "ruta proprie", "First path received": "singura rută" };

const labRoles = new Map();     // asn -> role, filled by the lab figure
const labPeers = new Map();     // link address -> role
const asNames = new Map();      // asn -> holder, filled by the 2008 figure

export function esc(value) {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

export function registerLab(routers, collector) {
  routers.forEach((r) => { labRoles.set(r.asn, r.role); labPeers.set(r.address, r.role); });
  if (collector) labPeers.set(collector.address, "collector");
}

export function registerNames(names) {
  Object.entries(names || {}).forEach(([asn, holder]) => asNames.set(Number(asn), holder));
}

export function roleOf(asn) {
  return labRoles.get(Number(asn)) || "";
}

export function nameOf(asn) {
  const role = roleOf(asn);
  if (role) return ROLE_NAME[role];
  return asNames.get(Number(asn)) || "";
}

// A holder name without its registry handle: "YOUTUBE - Google LLC" becomes "Google LLC".
export function shortName(name) {
  return (name || "").replace(/^[A-Z0-9-]+ - /, "").split(",")[0].trim();
}

// A vantage point by name: the lab router's role, or the RIS peer's address.
export function peerName(address) {
  const role = labPeers.get(address);
  return role ? ROLE_NAME[role] : address;
}

export function peerMarkup(address) {
  const role = labPeers.get(address);
  return role ? `<b class="${role}">${ROLE_NAME[role]}</b>` : `<span class="mono">${esc(address)}</span>`;
}

// A single AS as a chip, with its name when known.
export function asChip(asn, extra = "") {
  const role = roleOf(asn);
  const name = nameOf(asn);
  const title = name ? ` title="${esc(name)}"` : "";
  return `<span class="as ${role} ${extra}"${title}>AS${esc(asn)}</span>`;
}

// The AS path drawn as a chain. The origin is the last AS; a local route has none.
export function chain(asPath, localAsn) {
  const path = Array.isArray(asPath) ? asPath : [];
  if (!path.length) {
    return `<span class="chain">${asChip(localAsn, "origin local")}<span class="sep">(proprie)</span></span>`;
  }
  const chips = path.map((asn, i) => asChip(asn, i === path.length - 1 ? "origin" : ""));
  return `<span class="chain">${chips.join('<span class="sep">&rsaquo;</span>')}</span>`;
}

// A path in the lane views: consecutive repeats (prepending) fold into one chip with a
// count; the origin and the AS that hands the route to it carry the origin's role.
export function pathChain(path, role) {
  const folded = [];
  path.forEach((asn) => {
    const last = folded[folded.length - 1];
    if (last && last.asn === asn) last.times++;
    else folded.push({ asn, times: 1 });
  });
  const chips = folded.map(({ asn, times }, i) => {
    const kind = i === folded.length - 1 ? `origin ${role}` : i === folded.length - 2 ? `hand ${role}` : "";
    const repeat = times > 1 ? `<span class="times">&times;${times}</span>` : "";
    return `<span class="as ${kind}">${esc(asn)}${repeat}</span>`;
  });
  return `<span class="chain">${chips.join('<span class="sep">&rsaquo;</span>')}</span>`;
}

// Lab times are local (today's show the clock only); recorded incidents keep UTC.
export function fmtTime(iso, utc = false) {
  const t = new Date(iso);
  if (isNaN(t)) return esc(iso);
  if (utc) {
    const date = t.toLocaleDateString("ro-RO", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" });
    return `${date}, ${t.toISOString().slice(11, 19)} UTC`;
  }
  if (t.toDateString() === new Date().toDateString()) return t.toLocaleTimeString("ro-RO");
  return t.toLocaleDateString("ro-RO", { day: "numeric", month: "short", year: "numeric" }) + ", " + t.toLocaleTimeString("ro-RO");
}

export function fmtUtc(iso, withSeconds = true) {
  const t = new Date(iso);
  return isNaN(t) ? esc(iso) : t.toISOString().slice(11, withSeconds ? 19 : 16);
}

// A count with its noun; from 20 on, Romanian puts "de" between them (51 de alerte),
// except after 01-19 in the last two digits (101 alerte).
export function plural(n, one, many) {
  if (n === 1) return `1 ${one}`;
  const de = n >= 20 && (n % 100 === 0 || n % 100 >= 20) ? "de " : "";
  return `${n} ${de}${many}`;
}

export function pretty(data) {
  return esc(JSON.stringify(data, null, 2));
}
