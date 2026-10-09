// Figure 1: the lab as its routers report it, the hijack scenarios, and the kernel walk.

import { every, get, post } from "./api.js";
import { ROLE_NAME, REASON, chain, esc, pretty, registerLab } from "./format.js";
import { setLab } from "./status.js";

const $ = (id) => document.getElementById(id);
const DESTINATIONS = [
  ["203.0.113.1", "203.0.113.1, în sub-prefixul /25"],
  ["203.0.113.129", "203.0.113.129, în afara lui"],
];
const SCENARIO_TEXT = {
  exact: "anunță prefixul exact 203.0.113.0/24",
  subprefix: "anunță sub-prefixul 203.0.113.0/25",
  both: "anunță prefixul exact și sub-prefixul",
};
const VERDICT = {
  via: (h, name) => `trimite pachetul către <b class="${roleOfAddress(h.next_hop)}">${name}</b> <span class="mono">(${esc(h.next_hop)})</span>`,
  local: () => `<span class="verdict delivered">livrează pachetul local</span>: adresa este a lui`,
  direct: () => `<span class="verdict delivered">livrează pachetul direct pe legătură</span>`,
  blackhole: () => `<span class="verdict dropped">aruncă pachetul</span>: ruta sa este un blackhole`,
  unreachable: () => `<span class="verdict unreachable">nu are nicio rută</span>`,
  unknown: () => `<span class="verdict">răspuns necunoscut</span>`,
};
const OUTCOME = {
  delivered: ["delivered", "Pachetul ajunge la destinație."],
  dropped: ["dropped", "Pachetul dispare: a fost atras de un ruter care îl aruncă."],
  unreachable: ["unreachable", "Pachetul nu poate fi trimis: niciun ruter nu are o rută."],
  lost: ["lost", "Pachetul pleacă spre un ruter care nu este în laborator."],
  loop: ["loop", "Pachetul se învârte în buclă."],
  unknown: ["unknown", "Nucleul a dat un răspuns pe care pagina nu îl înțelege."],
};

export const lab = { routers: [], collector: null, byAddress: new Map() };
const listeners = [];

export function onLab(fn) {
  listeners.push(fn);
}

export function startLab() {
  fillSelect($("walk-from"), [["transit", "Tranzit"], ["victim", "Victimă"], ["attacker", "Atacator"]]);
  fillSelect($("walk-to"), DESTINATIONS);
  $("btn-hijack").addEventListener("click", () => act("trigger"));
  $("btn-reset").addEventListener("click", () => act("reset"));
  $("btn-walk").addEventListener("click", walk);
  every(2000, refresh);
}

function fillSelect(select, options) {
  select.innerHTML = options.map(([value, label]) => `<option value="${esc(value)}">${esc(label)}</option>`).join("");
}

async function refresh() {
  try {
    const data = await get("/api/lab");
    lab.routers = data.routers;
    lab.collector = data.collector;
    lab.byAddress = new Map(data.routers.map((r) => [r.address, r]));
    registerLab(data.routers, data.collector);
    renderRibs(data.routers);
    $("raw-lab").textContent = JSON.stringify(data, null, 2);
    setLab(data.routers.every((r) => r.reachable));
    listeners.forEach((fn) => fn(lab));
  } catch (e) {
    setLab(false);
    $("ribs").innerHTML = `<div class="empty">Laboratorul nu răspunde: ${esc(e.message)}</div>`;
  }
}

function roleOfAddress(address) {
  const router = lab.byAddress.get(address);
  return router ? router.role : "";
}

function renderRibs(routers) {
  const attackerAs = (routers.find((r) => r.role === "attacker") || {}).asn;
  $("ribs").innerHTML = routers.map((r) => ribOf(r, attackerAs)).join("");
}

function ribOf(r, attackerAs) {
  const perPrefix = {};
  r.paths.forEach((p) => { perPrefix[p.prefix] = (perPrefix[p.prefix] || 0) + 1; });
  const rows = r.paths.map((p) => {
    const bad = p.origin_as === attackerAs;
    const why = p.best && perPrefix[p.prefix] > 1 && p.reason ? `<div class="why">${esc(REASON[p.reason] || p.reason)}</div>` : "";
    return `
      <tr class="${bad ? "bad" : ""}" data-router="${r.role}" data-prefix="${esc(p.prefix)}">
        <td class="mono">${esc(p.prefix)}</td>
        <td><div class="pathline">${chain(p.as_path, r.asn)}<div><span class="state ${p.best ? "best" : "spare"}">${p.best ? "aleasă" : "rezervă"}</span>${why}</div></div></td>
      </tr>`;
  }).join("");
  const empty = `<tr><td colspan="2" class="empty">${r.reachable ? "Nicio rută." : "Ruter oprit."}</td></tr>`;
  return `
    <div class="rib">
      <h4><span class="${r.role}">${ROLE_NAME[r.role]}</span> <span class="addr">AS${r.asn}, ${esc(r.address)}</span></h4>
      <table class="data">
        <thead><tr><th>Prefix</th><th><div class="pathline"><span>Calea AS</span><span>Stare</span></div></th></tr></thead>
        <tbody>${rows || empty}</tbody>
      </table>
    </div>`;
}

async function act(kind) {
  const scenario = $("scenario").value;
  const msg = $("action-msg");
  const buttons = [$("btn-hijack"), $("btn-reset")];
  buttons.forEach((b) => { b.disabled = true; });
  msg.textContent = kind === "trigger" ? "se injectează..." : "se resetează...";
  try {
    const res = await post(`/api/lab/${kind}`, kind === "trigger" ? { scenario } : undefined);
    msg.innerHTML = kind === "trigger"
      ? `Atacatorul <b>${esc(SCENARIO_TEXT[scenario])}</b>. Urmărește tabelele și Figura 2.`
      : "Atacatorul și-a retras anunțurile. Tabelele revin la starea de bază.";
    if (!res.ok) msg.textContent = "Eroare: " + (res.error || "necunoscută");
  } catch (e) {
    msg.textContent = "Eroare: " + e.message;
  } finally {
    buttons.forEach((b) => { b.disabled = false; });
    setTimeout(refresh, 1500);
  }
}

async function walk() {
  const from = $("walk-from").value;
  const to = $("walk-to").value;
  const out = $("walk-result");
  $("btn-walk").disabled = true;
  out.innerHTML = '<div class="msg">se întreabă nucleele...</div>';
  try {
    const res = await post("/api/lab/trace", { from, to });
    const hops = res.hops.map((h) => {
      const next = lab.byAddress.get(h.next_hop);
      const name = next ? ROLE_NAME[next.role] : h.next_hop;
      return `<li><b class="${h.router}">${ROLE_NAME[h.router]}</b> ${VERDICT[h.kind](h, name)}</li>`;
    }).join("");
    const [cls, text] = OUTCOME[res.outcome] || OUTCOME.unknown;
    out.innerHTML = `<ol class="hops">${hops}</ol><p class="caption"><span class="${cls} verdict">${esc(text)}</span> De la <b>${ROLE_NAME[from]}</b> către <span class="mono">${esc(to)}</span>, după nucleele ruterelor, fără niciun pachet trimis.</p>`;
  } catch (e) {
    out.innerHTML = `<div class="msg">Eroare: ${esc(e.message)}</div>`;
  } finally {
    $("btn-walk").disabled = false;
  }
}
