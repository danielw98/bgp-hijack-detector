// Figure 1: the lab as its routers report it, drawn as a graph with its tables; the
// hijack scenarios; the packet simulation on the tables; and the kernel walk as proof.

import { every, get, post } from "./api.js";
import { ROLE_NAME, REASON, chain, esc, registerLab } from "./format.js";
import { createGraph } from "./graph.js";
import { simulate } from "./simulate.js";
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
const OUTCOME = {
  delivered: ["delivered", "Pachetul ajunge la destinație."],
  dropped: ["dropped", "Pachetul dispare: a fost atras de un ruter care îl aruncă."],
  unreachable: ["unreachable", "Pachetul nu poate fi trimis: ruterul nu are nicio rută."],
  lost: ["lost", "Pachetul pleacă spre un ruter care nu este în laborator."],
  loop: ["loop", "Pachetul se învârte în buclă."],
  unknown: ["unknown", "Răspuns pe care pagina nu îl înțelege."],
};
const VERDICT = {
  via: (h, name, role) => `trimite pachetul către <b class="${role}">${esc(name)}</b> <span class="mono">(${esc(h.next_hop)})</span>`,
  local: () => `<span class="verdict delivered">livrează pachetul local</span>: adresa este a lui`,
  direct: () => `<span class="verdict delivered">livrează pachetul direct pe legătură</span>`,
  blackhole: () => `<span class="verdict dropped">aruncă pachetul</span>: ruta sa este un blackhole`,
  unreachable: () => `<span class="verdict unreachable">nu are nicio rută</span>`,
  unknown: () => `<span class="verdict">răspuns necunoscut</span>`,
};

export const lab = { routers: [], collector: null, byAddress: new Map(), byRole: new Map() };
let graph = null;
let previous = null;          // the set of paths seen at the last poll
let stepped = null;           // a packet simulation advanced by hand: { steps, outcome, i, from, to }
let lastSim = null;           // the last finished simulation, for the agreement line
let busy = false;

export function startLab() {
  graph = createGraph($("graph"));
  fillSelect($("walk-from"), [["transit", "Tranzit"], ["victim", "Victimă"], ["attacker", "Atacator"]]);
  fillSelect($("walk-to"), DESTINATIONS);
  $("btn-hijack").addEventListener("click", () => act("trigger"));
  $("btn-reset").addEventListener("click", () => act("reset"));
  $("btn-send").addEventListener("click", () => play(false));
  $("btn-step").addEventListener("click", () => play(true));
  $("btn-walk").addEventListener("click", walk);
  ["walk-from", "walk-to"].forEach((id) => $(id).addEventListener("change", () => { stepped = null; lastSim = null; $("sim-result").innerHTML = ""; }));
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
    lab.byRole = new Map(data.routers.map((r) => [r.role, r]));
    registerLab(data.routers, data.collector);
    graph.render(data);
    renderRibs(data.routers);
    $("raw-lab").textContent = JSON.stringify(data, null, 2);
    setLab(data.routers.every((r) => r.reachable));
    announceChanges(data.routers);
  } catch (e) {
    setLab(false);
    $("ribs").innerHTML = `<div class="empty">Laboratorul nu răspunde: ${esc(e.message)}</div>`;
  }
}

// UPDATE and withdrawal tokens for every path that appeared or vanished since the
// last poll: along the AS path from its origin to the router, then on to the collector.
function announceChanges(routers) {
  const now = new Map();
  routers.forEach((r) => r.paths.forEach((p) => now.set(`${r.role}|${p.prefix}|${p.as_path.join(" ")}`, { router: r, path: p })));
  if (previous) {
    const label = (p) => p.prefix.replace("203.0.113.0", "");
    [...now].filter(([k]) => !previous.has(k)).forEach(([, { router, path }], i) => {
      const hops = hopsOf(router, path);
      setTimeout(() => graph.route(hops, `UPDATE ${label(path)}`, "update"), i * 150);
    });
    [...previous].filter(([k]) => !now.has(k)).forEach(([, { router, path }], i) => {
      const hops = hopsOf(router, path);
      setTimeout(() => graph.route(hops, `retras ${label(path)}`, "withdraw"), i * 150);
    });
  }
  previous = now;
}

function hopsOf(router, path) {
  const roles = path.as_path.map((asn) => (lab.routers.find((r) => r.asn === asn) || {}).role).filter(Boolean);
  const stops = [...roles.reverse(), router.role];
  const hops = [];
  for (let i = 0; i + 1 < stops.length; i++) if (stops[i] !== stops[i + 1]) hops.push([stops[i], stops[i + 1]]);
  if (path.best) hops.push([router.role, "collector"]);
  return hops;
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
      <tr class="${bad ? "bad" : ""}" data-router="${r.role}" data-prefix="${esc(p.prefix)}" data-best="${p.best}">
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
      ? `Atacatorul <b>${esc(SCENARIO_TEXT[scenario])}</b>. Urmărește mesajele de pe hartă, tabelele și Figura 2.`
      : "Atacatorul și-a retras anunțurile. Tabelele revin la starea de bază.";
    if (!res.ok) msg.textContent = "Eroare: " + (res.error || "necunoscută");
  } catch (e) {
    msg.textContent = "Eroare: " + e.message;
  } finally {
    buttons.forEach((b) => { b.disabled = false; });
    setTimeout(refresh, 1200);
  }
}

// The packet simulation: all steps at once, or one more step per press.
async function play(byHand) {
  if (busy) return;
  const from = $("walk-from").value;
  const to = $("walk-to").value;
  if (!stepped || stepped.from !== from || stepped.to !== to || stepped.i >= stepped.steps.length) {
    stepped = { ...simulate(lab, from, to), i: 0, from, to };
    $("sim-result").innerHTML = `<p class="caption">Un pachet de la <b>${ROLE_NAME[from]}</b> către <span class="mono">${esc(to)}</span>, după tabelele BGP de mai sus.</p><ol class="hops" id="sim-steps"></ol>`;
    clearHighlight();
  }
  busy = true;
  $("btn-send").disabled = $("btn-step").disabled = true;
  try {
    do {
      await showStep(stepped.steps[stepped.i], stepped);
      stepped.i += 1;
    } while (!byHand && stepped.i < stepped.steps.length);
    if (stepped.i >= stepped.steps.length) finishSimulation(stepped);
  } finally {
    busy = false;
    $("btn-send").disabled = $("btn-step").disabled = false;
  }
}

async function showStep(step, sim) {
  const list = $("sim-steps");
  const item = document.createElement("li");
  item.innerHTML = narrate(step, sim.to);
  list.appendChild(item);
  clearHighlight();
  if (step.chosen) {
    const row = $("ribs").querySelector(`tr[data-router="${step.router}"][data-prefix="${step.chosen.prefix}"][data-best="true"]`);
    if (row) row.classList.add("hilite");
  }
  graph.pulse(step.router);
  if (step.kind === "forward" && step.next) await graph.travel(step.router, step.next, "pachet", "packet");
}

function narrate(step, to) {
  const who = `<b class="${step.router}">${ROLE_NAME[step.router]}</b>`;
  const owner = (p) => (p.as_path.length ? ROLE_NAME[(lab.routers.find((r) => r.asn === p.origin_as) || {}).role] || `AS${p.origin_as}` : "propriul anunț");
  const options = step.candidates.map((p) => `<span class="mono">${esc(p.prefix)}</span> (${esc(owner(p))})`).join(" și ");
  if (step.kind === "unreachable") return `${who}: nicio rută nu acoperă <span class="mono">${esc(to)}</span>. <span class="verdict unreachable">Pachetul nu poate fi trimis.</span>`;
  const choice = step.candidates.length > 1
    ? `se potrivesc ${options}; câștigă <span class="mono">${esc(step.chosen.prefix)}</span>, prefixul mai specific`
    : `se potrivește ${options}`;
  if (step.kind === "delivered") return `${who}: ${choice}; ruta este a lui și adresa este pe propria interfață. <span class="verdict delivered">Pachetul este livrat.</span>`;
  if (step.kind === "blackhole") return `${who}: ${choice}; ruta este propriul anunț, dar adresa nu este a lui, iar ruta sa statică este un blackhole. <span class="verdict dropped">Pachetul este aruncat.</span>`;
  const nextName = step.next ? ROLE_NAME[step.next] : step.nextHop;
  const sender = step.chosen.as_path.length ? ROLE_NAME[(lab.routers.find((r) => r.asn === step.chosen.as_path[0]) || {}).role] : "";
  const shortcut = step.next && sender && sender !== nextName
    ? ` Anunțul a venit de la ${esc(sender)}, dar următorul salt este direct ${esc(nextName)}, pe segmentul partajat.`
    : "";
  return `${who}: ${choice}; următorul salt este <b class="${step.next || ""}">${esc(nextName)}</b> <span class="mono">(${esc(step.nextHop)})</span>.${shortcut}`;
}

function finishSimulation(sim) {
  const [cls, text] = OUTCOME[sim.outcome] || OUTCOME.unknown;
  $("sim-result").insertAdjacentHTML("beforeend", `<p class="caption"><span class="${cls} verdict">${esc(text)}</span></p>`);
  lastSim = sim;
  setTimeout(clearHighlight, 2500);
}

function clearHighlight() {
  $("ribs").querySelectorAll("tr.hilite").forEach((tr) => tr.classList.remove("hilite"));
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
      return `<li><b class="${h.router}">${ROLE_NAME[h.router]}</b> ${VERDICT[h.kind](h, name, next ? next.role : "")}</li>`;
    }).join("");
    const [cls, text] = OUTCOME[res.outcome] || OUTCOME.unknown;
    out.innerHTML = `<p class="caption">Nucleul fiecărui ruter, întrebat cu <span class="mono">ip route get</span>; niciun pachet nu este trimis.</p><ol class="hops">${hops}</ol><p class="caption"><span class="${cls} verdict">${esc(text)}</span> ${agreement(res, from, to)}</p>`;
  } catch (e) {
    out.innerHTML = `<div class="msg">Eroare: ${esc(e.message)}</div>`;
  } finally {
    $("btn-walk").disabled = false;
  }
}

function agreement(res, from, to) {
  if (!lastSim || lastSim.from !== from || lastSim.to !== to) return "";
  const simRouters = lastSim.steps.map((s) => s.router).join(" ");
  const realRouters = res.hops.map((h) => h.router).join(" ");
  return simRouters === realRouters && lastSim.outcome === res.outcome
    ? "Nucleele confirmă simularea de pe tabelele BGP: aceleași rutere, același rezultat."
    : "Nucleele spun altceva decât simularea: tabelele BGP s-au schimbat între timp sau nucleul rutează diferit.";
}
