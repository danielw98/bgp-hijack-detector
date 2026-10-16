// The tour: eight steps that tell the hijack on the simulator's map, one sentence and one
// change per step, in two sources. Recorded plays one real cycle captured from the lab
// (static/tour/cycle.json, written by scripts/record_tour.py): the step's snapshot goes on
// the map and its observed rows play, so it works with the lab down and never depends on
// another visitor. Live runs the step's action on the real routers and lets the simulator
// play what they report. The Free mode is the simulator as it is, with its own controls.
// "#tour-N" in the URL opens step N.

import { get, post } from "./api.js";
import { ALERT_NAME, esc, peerName } from "./format.js";
import { and, count, t, LOCALE } from "./i18n.js";

const CYCLE = "/static/tour/cycle.json";
const TOTAL = 8;
const AFTER_LIVE_MS = 3500;     // the routers report within a poll or two
const SETTLE_MS = 4000;         // a reset before a live step needs the routers to converge
const INSIDE = "203.0.113.1";

// What each step does: the lab action it needs, the walk it shows (from the router where
// the lesson is), the rows it replays.
const STEPS = {
  1: { action: "announce", fresh: true },
  2: { replays: 1, highlight: ["attacker", "203.0.113.0/24"] },
  3: { packet: { from: "attacker", to: INSIDE }, kernel: true, uses: 1 },
  4: { action: "trigger", scenario: "exact" },
  5: { action: "trigger", scenario: "subprefix", packet: { from: "transit", to: INSIDE }, kernel: true },
  6: { incident: true, uses: 5 },
  7: { action: "reset" },
  8: { action: "defence", packet: { from: "transit", to: INSIDE }, kernel: true },
};

export function mountTour(root, sim) {
  const $ = (name) => root.querySelector(`[data-tour="${name}"]`);
  const strip = $("strip");
  const panel = $("panel");
  const modes = [...root.querySelectorAll("[data-mode]")];
  let cycle = null;
  let mode = "tour";
  let source = "recorded";
  let step = 0;
  let busy = false;

  modes.forEach((b) => b.addEventListener("click", () => setMode(b.dataset.mode)));
  strip.addEventListener("click", (e) => { const li = e.target.closest("[data-step]"); if (li) go(Number(li.dataset.step)); });
  panel.addEventListener("click", (e) => {
    const b = e.target.closest("button[data-act]");
    if (!b) return;
    if (b.dataset.act === "back") go(step - 1);
    else if (b.dataset.act === "next") go(step + 1);
    else if (b.dataset.act === "again") go(step);
    else if (b.dataset.act === "source") setSource(b.dataset.source);
  });
  window.addEventListener("hashchange", fromHash);
  const base = document.documentElement.dataset.base ?? "";
  get(CYCLE.replace(/^/, base ? "" : ""))
    .catch(() => null)
    .then((data) => { cycle = data && data.steps ? data : null; if (!cycle) source = "live"; fromHash(); });

  function fromHash() {
    const m = location.hash.match(/^#tour-(\d)$/);
    if (m) { setMode("tour", false); go(Number(m[1])); }
    else if (!step) { setMode(mode, false); if (mode === "tour") go(1); }
  }

  function setMode(next, render = true) {
    mode = next;
    modes.forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.mode === mode)));
    root.classList.toggle("tour-mode", mode === "tour");
    root.classList.toggle("free-mode", mode === "free");
    if (mode === "free") { sim.setSource("live"); sim.transport.pause(); if (render) renderStrip(); return; }
    if (render && step) go(step);
  }

  function setSource(next) {
    if (next === "recorded" && !cycle) return;
    source = next;
    go(step);
  }

  // Shows step n: the picture before the eye, the sentence beside it, then what moves.
  async function go(n) {
    if (busy || n < 1 || n > TOTAL) return;
    const leavingDefence = source === "live" && step === TOTAL && n !== TOTAL;
    step = n;
    renderStrip();
    renderPanel();
    const spec = STEPS[n];
    busy = true;
    try {
      if (leavingDefence) await leaveDefence();
      if (source === "recorded") await playRecorded(n, spec); else await playLive(n, spec);
    } catch (e) {
      note(t("tour.live.unavailable", { message: e.message }), "bad");
    } finally {
      busy = false;
    }
  }

  // The live defence leaves the transit validating and the attacker announcing; undo both.
  async function leaveDefence() {
    await post("/api/lab/rov", { on: false });
    await sim.act("reset");
  }

  // What the transit received and refused, when the snapshot says so.
  function renderRejected(snapshot) {
    const slot = $("extra");
    const transit = snapshot.routers.find((r) => r.role === "transit");
    if (!slot || !transit || !transit.rejected || !transit.rejected.length) return;
    const list = transit.rejected.map((r) => `<span class="mono">${esc(r.prefix)}</span>`).join(and());
    slot.innerHTML = t("tour.s8.rejected", { list });
  }

  async function playRecorded(n, spec) {
    const rec = recordOf(n);
    if (!rec) { note(t("tour.missing")); sim.setSource("live"); return; }
    sim.setSource("fixture", rec.after);
    if (spec.highlight) sim.highlight(...spec.highlight);
    const played = spec.replays ? recordOf(spec.replays) : rec;
    const fresh = spec.fresh || (spec.replays && STEPS[spec.replays].fresh);
    // A fresh announcement plays from empty tables, without the withdrawal that made room for it.
    const rows = fresh ? played.rows.filter((r) => !r.withdrawn) : played.rows;
    const before = fresh ? { routers: played.before.routers.map((r) => ({ ...r, paths: [] })) } : played.before;
    if (rows && rows.length) sim.playRows(rows, before);
    if (spec.packet) sim.queuePacket(spec.packet.from, spec.packet.to);
    const trace = spec.kernel && rec.trace && rec.trace[spec.packet.from] && rec.trace[spec.packet.from][spec.packet.to];
    if (trace) sim.queueKernel(trace, spec.packet.from, spec.packet.to);
    if (spec.incident) renderIncident(rec.alerts || [], firstClaimAt(4));
    if (n === TOTAL) renderRejected(rec.after);
    note(t("tour.recorded.note", { date: new Date(cycle.recorded_at).toLocaleDateString(LOCALE) }));
    sim.transport.resume();
  }

  // The step's record: its own, or the step it builds on.
  function recordOf(n) {
    const spec = STEPS[n];
    return cycle.steps[String(n)] || (spec.uses ? cycle.steps[String(spec.uses)] : null) || (spec.replays ? cycle.steps[String(spec.replays)] : null);
  }

  // When the attacker's command of step n was given, the moment the detector's delay counts from.
  function firstClaimAt(n) {
    const rec = cycle.steps[String(n)];
    return rec && rec.started_at ? Date.parse(rec.started_at) : null;
  }

  async function playLive(n, spec) {
    sim.setSource("live");
    note(t("tour.live.note"));
    if (spec.action === "announce" || spec.action === "trigger" && spec.scenario === "exact") await restFirst();
    if (spec.action === "announce") await post("/api/lab/announce");
    if (spec.action === "trigger") await sim.act("trigger", { scenario: spec.scenario });
    if (spec.action === "reset") await sim.act("reset");
    if (spec.action === "defence") {
      await post("/api/lab/rov", { on: true });
      await sim.act("trigger", { scenario: "both" });
    }
    if (spec.highlight) sim.highlight(...spec.highlight);
    if (spec.packet) setTimeout(() => { sim.queuePacket(spec.packet.from, spec.packet.to); sim.transport.resume(); }, spec.action ? AFTER_LIVE_MS : 0);
    if (spec.incident) {
      const alerts = (await get("/api/alerts?limit=50")).filter((a) => a.source === "lab");
      renderIncident(alerts, null);
    }
    if (n === TOTAL) setTimeout(async () => { await sim.refresh(); renderRejected(await get("/api/lab")); }, AFTER_LIVE_MS);
    note(spec.action ? t("tour.live.running") : t("tour.live.note"));
  }

  // A live step that starts a story needs the lab at rest.
  async function restFirst() {
    if (!sim.state().claimed.length) return;
    note(t("tour.live.hijacked"), "bad");
    await sim.act("reset");
    note(t("tour.live.resetting"));
    await new Promise((resolve) => setTimeout(resolve, SETTLE_MS));
    await sim.refresh();
  }

  function renderStrip() {
    strip.innerHTML = Array.from({ length: TOTAL }, (_, i) => i + 1).map((n) => `
      <li class="${n < step ? "done" : n === step ? "current" : "next"}">
        <button type="button" data-step="${n}" aria-current="${n === step ? "step" : "false"}"><span class="n">${n}</span><span class="label">${esc(t(`tour.s${n}.label`))}</span></button>
      </li>`).join("");
  }

  function renderPanel() {
    const sources = cycle
      ? `<div class="seg-control source" role="group" aria-label="${esc(t("tour.source.label"))}">
          <button type="button" data-act="source" data-source="recorded" aria-pressed="${source === "recorded"}">${esc(t("tour.source.recorded"))}</button>
          <button type="button" data-act="source" data-source="live" aria-pressed="${source === "live"}">${esc(t("tour.source.live"))}</button>
        </div>`
      : "";
    panel.innerHTML = `
      <header><span class="of">${esc(t("tour.step.of", { n: step, total: TOTAL }))}</span>${sources}</header>
      <h3>${esc(t(`tour.s${step}.title`))}</h3>
      <p class="text">${esc(t(`tour.s${step}.text`))}</p>
      <p class="extra" data-tour="extra"></p>
      <p class="note" data-tour="note"></p>
      <div class="nav">
        <button type="button" class="btn outline" data-act="back"${step === 1 ? " disabled" : ""}>${esc(t("tour.back"))}</button>
        <button type="button" class="btn" data-act="next"${step === TOTAL ? " disabled" : ""}>${esc(t("tour.next"))}</button>
        <button type="button" class="btn outline small" data-act="again">${esc(t("tour.again"))}</button>
      </div>`;
  }

  function note(text, kind = "") {
    const slot = $("note");
    if (!slot) return;
    slot.textContent = text;
    slot.className = `note ${kind}`;
  }

  // What the detector raised in this cycle, with the delay from the attacker's first row.
  function renderIncident(alerts, claimAt) {
    const slot = $("extra");
    if (!slot) return;
    if (!alerts.length) { slot.textContent = t("tour.s6.none"); return; }
    const first = alerts.reduce((a, b) => (Date.parse(a.raised_at) < Date.parse(b.raised_at) ? a : b));
    const kinds = listed([...new Set(alerts.map((a) => ALERT_NAME[a.alert_type] || a.alert_type))]);
    const peers = listed([...new Set(alerts.map((a) => peerName(a.peer)))]);
    const delta = claimAt ? `${((Date.parse(first.raised_at) - claimAt) / 1000).toFixed(2)} s` : "";
    slot.innerHTML = `${esc(kinds)}. ${claimAt ? esc(t("tour.s6.first", { delta, alerts: count(alerts.length, "incident.alerts"), peers })) : ""}`;
  }

  // "a, b and c".
  function listed(items) {
    return items.length < 2 ? items.join("") : `${items.slice(0, -1).join(", ")}${and()}${items[items.length - 1]}`;
  }
}
