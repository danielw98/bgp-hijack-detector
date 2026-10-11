// The simulator: the lab as its routers report it, drawn on the canvas with its tables
// in the inspector; the hijack scenarios run in the real routers; the packet walk on
// the tables; the kernel walk as proof. Everything that moves is an event of the
// transport: one hop of an UPDATE, a packet or a kernel answer. A click on the current
// message, its row or a router opens a popover with what the hop carried and decided.

import { every, get, post } from "./api.js";
import { ALERT_NAME, ROLE_NAME, REASON, asChip, chain, esc, fmtTime, registerLab } from "./format.js";
import { createCanvas } from "./canvas.js";
import { createTransport } from "./transport.js";
import { contains, prefixLength, simulate } from "./simulate.js";
import { createPopover } from "./popover.js";
import { setLab } from "./status.js";

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
  delivered: "Pachetul ajunge la destinație.",
  dropped: "Pachetul dispare: a fost atras de un ruter care îl aruncă.",
  unreachable: "Pachetul nu poate fi trimis: ruterul nu are nicio rută.",
  lost: "Pachetul pleacă spre un ruter care nu este în simulator.",
  loop: "Pachetul se învârte în buclă.",
  unknown: "Răspuns pe care pagina nu îl înțelege.",
};
const POPOVER_TITLE = { update: "UPDATE", withdraw: "RETRAS", packet: "pachet", kernel: "nucleu" };
const ALERTS_LOOKED_AT = 200;
const STEP_VERDICT = { delivered: "delivered", blackhole: "dropped", unreachable: "unreachable" };
const KERNEL_VERDICT = { local: "delivered", direct: "delivered", blackhole: "dropped", unreachable: "unreachable" };
const KERNEL_TEXT = {
  via: (h, name) => `trimite pachetul către <b>${esc(name)}</b> <span class="mono">(${esc(h.next_hop)})</span>`,
  local: () => "livrează pachetul local: adresa este a lui",
  direct: () => "livrează pachetul direct pe legătură",
  blackhole: () => "aruncă pachetul: ruta sa este un blackhole",
  unreachable: () => "nu are nicio rută",
  unknown: () => "răspuns necunoscut",
};

export const lab = { routers: [], collector: null, byAddress: new Map(), byRole: new Map() };

export function mountSimulator(root) {
  const $ = (name) => root.querySelector(`[data-sim="${name}"]`);
  const canvas = createCanvas($("canvas"));
  const transport = createTransport({
    canvas,
    list: $("events"),
    play: $("play"),
    back: $("back"),
    step: $("step"),
    clear: $("clear"),
    speed: $("speed"),
    speedLabel: $("speed-label"),
    count: $("count"),
    now: $("now"),
    onShow,
  });
  const popover = createPopover();
  let openNext = false;
  let monitored = [];
  get("/api/monitored").then((rows) => { monitored = rows; }).catch(() => {});
  let previous = null;        // the paths seen at the last poll
  let lastSim = null;         // the last packet walk, for the agreement line

  wireTabs(root);
  wireOptions($("options"), $("options-panel"));
  fillSelect($("from"), [["transit", "Tranzit"], ["victim", "Victimă"], ["attacker", "Atacator"]]);
  fillSelect($("to"), DESTINATIONS);
  $("hijack").addEventListener("click", () => act("trigger"));
  $("reset").addEventListener("click", () => act("reset"));
  $("send").addEventListener("click", sendPacket);
  $("ask").addEventListener("click", askKernels);
  $("events").addEventListener("click", (e) => { if (e.target.closest("li[data-i]")) openNext = true; });
  $("canvas").addEventListener("click", openFromMap);
  $("canvas").addEventListener("keydown", (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); openFromMap(e); } });
  document.addEventListener("click", (e) => {
    const button = e.target.closest(".popover [data-pop]");
    if (button) $(button.dataset.pop).click();
  });
  every(2000, refresh);

  function onShow(ev, index, total) {
    highlightRow(ev);
    if (!ev) { popover.close(); return; }
    if (openNext || popover.isOpen()) openEvent(ev, index, total);
    openNext = false;
  }

  function openFromMap(e) {
    const node = e.target.closest(".node[data-role]");
    if (node && lab.byRole.has(node.dataset.role)) { popover.open(node, describeRouter(lab.byRole.get(node.dataset.role))); return; }
    if (!e.target.closest(".token, .marker")) return;
    const now = transport.current();
    if (!now) return;
    transport.pause();
    openEvent(now.ev, now.index, now.total);
  }

  function openEvent(ev, index, total) {
    const anchor = $("canvas").querySelector(".tokens .token") || $("canvas");
    popover.open(anchor, describeEvent(ev, index, total));
    if (ev.to === "collector" && ev.kind === "update") fillAlerts(ev);
  }

  async function refresh() {
    try {
      const data = await get("/api/lab");
      lab.routers = data.routers;
      lab.collector = data.collector;
      lab.byAddress = new Map(data.routers.map((r) => [r.address, r]));
      lab.byRole = new Map(data.routers.map((r) => [r.role, r]));
      registerLab(data.routers, data.collector);
      canvas.render(data);
      renderTables(data.routers);
      document.getElementById("raw-lab").textContent = JSON.stringify(data, null, 2);
      setLab(data.routers.every((r) => r.reachable));
      announceChanges(data.routers);
    } catch (e) {
      setLab(false);
      $("tables").innerHTML = `<div class="empty">Ruterele nu răspund: ${esc(e.message)}</div>`;
    }
  }

  // One event per hop for every path that appeared or vanished since the last poll:
  // along the AS path from its origin to the router, then on to the collector.
  function announceChanges(routers) {
    const now = new Map();
    routers.forEach((r) => r.paths.forEach((p) => now.set(`${r.role}|${p.prefix}|${p.as_path.join(" ")}`, { router: r, path: p })));
    if (previous) {
      const hops = [];
      [...now].filter(([k]) => !previous.has(k)).forEach(([, { router, path }]) => hops.push(...hopsOf(router, path, "update")));
      [...previous].filter(([k]) => !now.has(k)).forEach(([, { router, path }]) => hops.push(...hopsOf(router, path, "withdraw")));
      const seen = new Set();
      const batch = hops
        .sort((a, b) => a.depth - b.depth || a.prefix.localeCompare(b.prefix))
        .filter((h) => !seen.has(h.key) && seen.add(h.key))
        .map(hopEvent);
      if (batch.length) transport.enqueue(batch);
    }
    previous = now;
  }

  // The hops of one path, from its origin to the router and on to the collector, each
  // with its distance from the origin; the same hop seen from two routers has one key.
  function hopsOf(router, path, kind) {
    const roles = path.as_path.map((asn) => (lab.routers.find((r) => r.asn === asn) || {}).role).filter(Boolean);
    const stops = [...roles.reverse(), router.role];
    const hops = [];
    for (let i = 0; i + 1 < stops.length; i++) {
      if (stops[i] !== stops[i + 1]) hops.push({ kind, from: stops[i], to: stops[i + 1], prefix: path.prefix, path: path.as_path, depth: hops.length });
    }
    if (path.best) hops.push({ kind, from: router.role, to: "collector", prefix: path.prefix, path: path.as_path, depth: hops.length + 0.5 });
    return hops.map((h) => ({ ...h, key: `${h.kind}|${h.from}|${h.to}|${h.prefix}` }));
  }

  function hopEvent({ kind, from, to, prefix, path }) {
    const verb = kind === "update" ? "anunță" : "retrage";
    return event(kind, from, to, prefix,
      to === "collector"
        ? `${ROLE_NAME[from]} raportează colectorului <span class="mono">${esc(prefix)}</span>`
        : `${ROLE_NAME[from]} ${verb} <span class="mono">${esc(prefix)}</span> către ${ROLE_NAME[to]}`,
      { path });
  }

  function event(kind, from, to, label, detail, extra = {}) {
    return { kind, from, to, fromName: ROLE_NAME[from], toName: to ? ROLE_NAME[to] : "", label, detail, ...extra };
  }

  function renderTables(routers) {
    const attackerAs = (routers.find((r) => r.role === "attacker") || {}).asn;
    const bodies = routers.map((r) => {
      const perPrefix = {};
      r.paths.forEach((p) => { perPrefix[p.prefix] = (perPrefix[p.prefix] || 0) + 1; });
      const rows = r.paths.map((p) => {
        const why = p.best && perPrefix[p.prefix] > 1 && p.reason ? `<div class="why">${esc(REASON[p.reason] || p.reason)}</div>` : "";
        return `<tr class="${p.origin_as === attackerAs ? "bad" : ""}" data-router="${r.role}" data-prefix="${esc(p.prefix)}" data-best="${p.best}">
          <td class="mono">${esc(p.prefix)}</td>
          <td>${chain(p.as_path, r.asn)}</td>
          <td><span class="state ${p.best ? "best" : "spare"}">${p.best ? "aleasă" : "rezervă"}</span>${why}</td>
        </tr>`;
      }).join("");
      const empty = `<tr><td colspan="3" class="empty">${r.reachable ? "Nicio rută." : "Ruter oprit."}</td></tr>`;
      return `<tbody class="${r.role}">
        <tr><th colspan="3" class="router"><span class="${r.role}">${ROLE_NAME[r.role]}</span> <span class="addr">AS${esc(r.asn)}, ${esc(r.address)}</span></th></tr>
        ${rows || empty}
      </tbody>`;
    }).join("");
    $("tables").innerHTML = `<table class="data rib"><thead><tr><th>Prefix</th><th>Calea AS</th><th>Stare</th></tr></thead>${bodies}</table>`;
  }

  function highlightRow(ev) {
    $("tables").querySelectorAll("tr.hilite").forEach((tr) => tr.classList.remove("hilite"));
    if (!ev || !ev.row) return;
    const row = $("tables").querySelector(`tr[data-router="${ev.row.router}"][data-prefix="${ev.row.prefix}"][data-best="true"]`);
    if (row) { row.classList.add("hilite"); row.scrollIntoView({ block: "nearest" }); }
  }

  async function act(kind) {
    const scenario = $("scenario").value;
    const msg = $("now");
    const buttons = [$("hijack"), $("reset")];
    buttons.forEach((b) => { b.disabled = true; });
    msg.textContent = kind === "trigger" ? "se injectează..." : "se retrag...";
    try {
      const res = await post(`/api/lab/${kind}`, kind === "trigger" ? { scenario } : undefined);
      msg.innerHTML = kind === "trigger"
        ? `Atacatorul <b>${esc(SCENARIO_TEXT[scenario])}</b>. Mesajele urmează pe hartă.`
        : "Atacatorul și-a retras anunțurile. Tabelele revin la starea de bază.";
      if (!res.ok) msg.textContent = "Eroare: " + (res.error || "necunoscută");
      transport.resume();
    } catch (e) {
      msg.textContent = "Eroare: " + e.message;
    } finally {
      buttons.forEach((b) => { b.disabled = false; });
      setTimeout(refresh, 1200);
    }
  }

  // The packet walk on the BGP tables, one event per router consulted.
  function sendPacket() {
    const from = $("from").value;
    const to = $("to").value;
    const sim = simulate(lab, from, to);
    const batch = sim.steps.map((step, i) => {
      const last = i === sim.steps.length - 1;
      const verdict = STEP_VERDICT[step.kind] || (last ? sim.outcome : null);
      const detail = narrate(step, to) + (last ? ` <b class="verdict ${verdict}">${esc(OUTCOME[sim.outcome] || OUTCOME.unknown)}</b>` : "");
      return event("packet", step.router, step.next, to, detail, { verdict, step, outcome: last ? sim.outcome : null, row: step.chosen && { router: step.router, prefix: step.chosen.prefix } });
    });
    lastSim = { ...sim, from, to };
    transport.enqueue(batch);
    transport.resume();
  }

  function narrate(step, to) {
    const who = `<b class="${step.router}">${ROLE_NAME[step.router]}</b>`;
    const owner = (p) => (p.as_path.length ? ROLE_NAME[(lab.routers.find((r) => r.asn === p.origin_as) || {}).role] || `AS${p.origin_as}` : "propriul anunț");
    const options = step.candidates.map((p) => `<span class="mono">${esc(p.prefix)}</span> (${esc(owner(p))})`).join(" și ");
    if (step.kind === "unreachable") return `${who}: nicio rută nu acoperă <span class="mono">${esc(to)}</span>.`;
    const choice = step.candidates.length > 1
      ? `se potrivesc ${options}; câștigă <span class="mono">${esc(step.chosen.prefix)}</span>, prefixul mai specific`
      : `se potrivește ${options}`;
    if (step.kind === "delivered") return `${who}: ${choice}; ruta este a lui și adresa este pe propria interfață.`;
    if (step.kind === "blackhole") return `${who}: ${choice}; ruta este propriul anunț, dar adresa nu este a lui, iar ruta sa statică este un blackhole.`;
    const nextName = step.next ? ROLE_NAME[step.next] : step.nextHop;
    const sender = step.chosen.as_path.length ? ROLE_NAME[(lab.routers.find((r) => r.asn === step.chosen.as_path[0]) || {}).role] : "";
    const shortcut = step.next && sender && sender !== nextName
      ? ` Anunțul a venit de la ${esc(sender)}, dar următorul salt este direct ${esc(nextName)}, pe segmentul partajat.`
      : "";
    return `${who}: ${choice}; următorul salt este <b>${esc(nextName)}</b> <span class="mono">(${esc(step.nextHop)})</span>.${shortcut}`;
  }

  // The kernel walk: each router's real routing decision, asked with ip route get.
  async function askKernels() {
    const from = $("from").value;
    const to = $("to").value;
    const btn = $("ask");
    const msg = $("now");
    btn.disabled = true;
    msg.textContent = "se întreabă nucleele...";
    try {
      const res = await post("/api/lab/trace", { from, to });
      const batch = res.hops.map((h, i) => {
        const next = lab.byAddress.get(h.next_hop);
        const name = next ? ROLE_NAME[next.role] : h.next_hop;
        const last = i === res.hops.length - 1;
        const verdict = KERNEL_VERDICT[h.kind] || (last ? res.outcome : null);
        const text = (KERNEL_TEXT[h.kind] || KERNEL_TEXT.unknown)(h, name);
        const detail = `<b class="${h.router}">${ROLE_NAME[h.router]}</b> ${text}` +
          (last ? ` <b class="verdict ${verdict}">${esc(OUTCOME[res.outcome] || OUTCOME.unknown)}</b> ${agreement(res, from, to)}` : "");
        return event("kernel", h.router, next ? next.role : null, to, detail, { verdict, hop: h, outcome: last ? res.outcome : null, agrees: last ? agreement(res, from, to) : "" });
      });
      msg.textContent = "Nucleele au răspuns; niciun pachet nu a fost trimis.";
      transport.enqueue(batch);
      transport.resume();
    } catch (e) {
      msg.textContent = "Eroare: " + e.message;
    } finally {
      btn.disabled = false;
    }
  }

  // What a popover shows for an event: the hop's message and what it decided.
  function describeEvent(ev, index, total) {
    const report = ev.to === "collector";
    const content = {
      kind: report ? "report" : ev.kind,
      title: report ? (ev.kind === "withdraw" ? "Raport BMP, retragere" : "Raport BMP") : POPOVER_TITLE[ev.kind],
      route: ev.toName ? `${ev.fromName} → ${ev.toName}` : ev.fromName,
      position: `${index + 1} / ${total}`,
      fields: [],
      footer: `<button class="btn outline small" data-pop="back"${index === 0 ? " disabled" : ""}>Înapoi</button><button class="btn small" data-pop="step"${index === total - 1 ? " disabled" : ""}>Pas</button>`,
    };
    if (ev.kind === "packet") content.fields = packetFields(ev);
    else if (ev.kind === "kernel") content.fields = kernelFields(ev);
    else if (report) content.fields = reportFields(ev);
    else content.fields = messageFields(ev);
    return content;
  }

  // The AS path as it travels on this hop: from the sender's AS to the origin.
  function carried(ev) {
    const sender = lab.byRole.get(ev.from);
    const at = sender ? ev.path.indexOf(sender.asn) : -1;
    return at < 0 ? ev.path : ev.path.slice(at);
  }

  function pathOnHop(asPath) {
    const chips = asPath.map((asn, i) => asChip(asn, [i === 0 ? "added" : "", i === asPath.length - 1 ? "origin" : ""].join(" ")));
    return `<span class="chain">${chips.join('<span class="sep">&rsaquo;</span>')}</span>`;
  }

  function messageFields(ev) {
    const path = carried(ev);
    const withdrawn = ev.kind === "withdraw";
    return [
      [withdrawn ? "Prefix retras" : "Prefix", `<b class="mono">${esc(ev.label)}</b>`],
      [withdrawn ? "Calea care dispare" : "Calea AS pe acest salt", pathOnHop(path)],
      ["Origine", originText(ev.label, path[path.length - 1])],
      withdrawn ? [`Ce rămâne la ${ev.toName}`, remaining(ev.to, ev.label)] : [`Ce face ${ev.toName}`, decision(ev.to, ev.label, path)],
    ];
  }

  function reportFields(ev) {
    const fields = [["Ruta din Loc-RIB", `<b class="mono">${esc(ev.label)}</b>`], ["Calea AS", chain(ev.path, (lab.byRole.get(ev.from) || {}).asn)]];
    return ev.kind === "withdraw"
      ? [...fields, ["Ce află colectorul", "Ruta a ieșit din tabela aleasă a ruterului."]]
      : [...fields, ["Verdictul detectorului", "<span data-alerts>se caută alertele...</span>"]];
  }

  function packetFields(ev) {
    const { step } = ev;
    const owner = (p) => (p.as_path.length ? ROLE_NAME[(lab.routers.find((r) => r.asn === p.origin_as) || {}).role] || `AS${p.origin_as}` : "propriul anunț");
    const fields = [["Destinație", `<b class="mono">${esc(ev.label)}</b>`]];
    if (!step.candidates.length) return [...fields, ["Rânduri care se potrivesc", "niciunul: nicio rută nu acoperă adresa"]];
    fields.push(["Rânduri care se potrivesc", step.candidates.map((p) => `<span class="mono">${esc(p.prefix)}</span> (${esc(owner(p))})`).join(" și ")]);
    fields.push(["Câștigă", `<span class="mono">${esc(step.chosen.prefix)}</span>${step.candidates.length > 1 ? ", prefixul mai specific" : ""}`]);
    if (step.nextHop) fields.push(["Următorul salt", `<b class="mono">${esc(step.next ? ROLE_NAME[step.next] : "")} ${esc(step.nextHop)}</b>`]);
    if (ev.outcome) fields.push(["Rezultat", esc(OUTCOME[ev.outcome] || OUTCOME.unknown)]);
    return fields;
  }

  function kernelFields(ev) {
    const h = ev.hop;
    const answer = h.kind === "via" ? `${ev.label} via ${h.next_hop} dev ${h.dev}` : h.kind === "local" ? `local ${ev.label} dev ${h.dev}` : h.kind;
    const fields = [[`ip route get ${ev.label}`, `<b class="mono">${esc(answer)}</b>`]];
    if (ev.outcome) fields.push(["Rezultat", esc(OUTCOME[ev.outcome] || OUTCOME.unknown)]);
    if (ev.agrees) fields.push(["Față de simulare", esc(ev.agrees)]);
    return fields;
  }

  // The origin against the most specific monitored prefix that covers the prefix.
  function originText(prefix, origin) {
    const base = prefix.split("/")[0];
    const covering = monitored.filter((m) => contains(m.prefix, base) && prefixLength(m.prefix) <= prefixLength(prefix));
    if (!covering.length) return `AS${esc(origin)}`;
    const m = covering.reduce((a, b) => (prefixLength(b.prefix) > prefixLength(a.prefix) ? b : a));
    return m.expected_origins.includes(origin)
      ? `AS${esc(origin)}, așteptată.`
      : `<b class="bad">AS${esc(origin)}, neașteptată</b>: <span class="mono">${esc(m.prefix)}</span> este al AS${m.expected_origins.map(esc).join(", AS")}.`;
  }

  function decision(role, prefix, path) {
    const router = lab.byRole.get(role);
    const p = router && router.paths.find((q) => q.prefix === prefix && q.as_path.join(" ") === path.join(" "));
    if (!p) return `Ruta nu mai este în tabela lui ${esc(ROLE_NAME[role])}.`;
    const why = p.reason ? ` (${esc(REASON[p.reason] || p.reason)})` : "";
    return p.best ? `O alege${why}.` : `O ține ca rezervă; alege altă cale${why}.`;
  }

  function remaining(role, prefix) {
    const router = lab.byRole.get(role);
    const best = router && router.paths.find((q) => q.prefix === prefix && q.best);
    return best ? `<span class="mono">${esc(prefix)}</span> prin ${chain(best.as_path, router.asn)}` : `Nicio rută pentru <span class="mono">${esc(prefix)}</span>.`;
  }

  // The alerts the detector raised for this route as this router reported it.
  async function fillAlerts(ev) {
    const router = lab.byRole.get(ev.from);
    const origin = ev.path.length ? ev.path[ev.path.length - 1] : router.asn;
    let text;
    try {
      const alerts = await get(`/api/alerts?limit=${ALERTS_LOOKED_AT}`);
      const mine = alerts.filter((a) => a.source === "lab" && a.prefix === ev.label && a.observed_origin === origin && a.peer === router.address);
      text = mine.length
        ? `Alertă ${[...new Set(mine.map((a) => ALERT_NAME[a.alert_type] || a.alert_type))].join(" și ")}, ridicată la ${esc(fmtTime(mine[0].raised_at))}.`
        : "Nicio alertă pentru această rută.";
    } catch (e) {
      text = "Alertele nu se pot citi acum.";
    }
    const slot = document.querySelector(".popover [data-alerts]");
    if (slot) slot.innerHTML = text;
  }

  function describeRouter(r) {
    const peer = (s) => `${esc(ROLE_NAME[(lab.byAddress.get(s.peer) || {}).role] || s.peer)} <span class="mono">${esc(s.peer)}</span>: ${s.established ? "stabilită" : '<b class="bad">căzută</b>'}`;
    const rows = r.paths.map((p) => `<tr><td class="mono">${esc(p.prefix)}</td><td>${chain(p.as_path, r.asn)}</td><td>${p.best ? "aleasă" : "rezervă"}</td></tr>`).join("");
    return {
      kind: r.role,
      title: ROLE_NAME[r.role],
      route: `AS${r.asn}, ${r.address}`,
      fields: [
        ["Sesiuni BGP", r.sessions.map(peer).join("<br>") || "niciuna"],
        ["Raportare BMP", r.reporting ? "raportează colectorului" : '<b class="bad">nu raportează</b>'],
        ["Tabela BGP", rows ? `<table class="mini">${rows}</table>` : (r.reachable ? "nicio rută" : "ruter oprit")],
      ],
    };
  }

  function agreement(res, from, to) {
    if (!lastSim || lastSim.from !== from || lastSim.to !== to) return "";
    const simRouters = lastSim.steps.map((s) => s.router).join(" ");
    const realRouters = res.hops.map((h) => h.router).join(" ");
    return simRouters === realRouters && lastSim.outcome === res.outcome
      ? "Nucleele confirmă simularea de pe tabelele BGP: aceleași rutere, același rezultat."
      : "Nucleele spun altceva decât simularea: tabelele s-au schimbat între timp sau nucleul rutează diferit.";
  }
}

function fillSelect(select, options) {
  select.innerHTML = options.map(([value, label]) => `<option value="${esc(value)}">${esc(label)}</option>`).join("");
}

// The options panel opens under its button and closes on Escape or a click outside.
function wireOptions(button, panel) {
  const show = (open) => { panel.hidden = !open; button.setAttribute("aria-expanded", String(open)); };
  button.addEventListener("click", () => show(panel.hidden));
  document.addEventListener("click", (e) => { if (!panel.hidden && !button.parentElement.contains(e.target)) show(false); });
  document.addEventListener("keydown", (e) => { if (e.key === "Escape" && !panel.hidden) { show(false); button.focus(); } });
}

function wireTabs(root) {
  const tabs = [...root.querySelectorAll("[data-tab]")];
  const panels = [...root.querySelectorAll("[data-panel]")];
  tabs.forEach((tab) => tab.addEventListener("click", () => {
    tabs.forEach((t) => { t.classList.toggle("current", t === tab); t.setAttribute("aria-selected", String(t === tab)); });
    panels.forEach((p) => { p.hidden = p.dataset.panel !== tab.dataset.tab; });
  }));
}
