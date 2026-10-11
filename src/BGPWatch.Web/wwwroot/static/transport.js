// The simulator's event queue and its playback. Every hop of a message, a packet or a
// kernel answer is one event between two nodes, listed in order. The transport plays
// the queue at the chosen speed, one step at a time, or back to an earlier event;
// while it plays, events that arrive run as they come.

import { esc } from "./format.js";

export const SPEEDS = [0.25, 0.5, 1, 2, 4];
const HOP_MS = 1400;
const KIND_NAME = { update: "UPDATE", withdraw: "retragere", packet: "pachet", kernel: "nucleu" };

export function createTransport({ canvas, list, play, back, step, clear, speed, speedLabel, count, onShow }) {
  const events = [];
  let cursor = -1;
  let auto = true;
  let running = false;
  let speedIndex = SPEEDS.indexOf(1);

  play.addEventListener("click", () => { auto = !auto; renderControls(); if (auto) run(); });
  step.addEventListener("click", () => { auto = false; renderControls(); forward(); });
  back.addEventListener("click", rewind);
  clear.addEventListener("click", reset);
  speed.addEventListener("input", () => { speedIndex = Number(speed.value); speedLabel.textContent = SPEEDS[speedIndex] + "x"; });
  list.addEventListener("click", (e) => {
    const li = e.target.closest("li[data-i]");
    if (!li || running) return;
    auto = false;
    renderControls();
    show(Number(li.dataset.i), false);
  });
  renderControls();
  renderList();

  function enqueue(batch) {
    const at = new Date();
    batch.forEach((ev) => events.push({ ...ev, at }));
    renderList();
    if (auto) run();
  }

  function resume() {
    auto = true;
    renderControls();
    run();
  }

  async function run() {
    if (running) return;
    running = true;
    renderControls();
    try {
      while (auto && cursor + 1 < events.length) await show(cursor + 1, true);
    } finally {
      running = false;
      renderControls();
    }
  }

  async function forward() {
    if (running || cursor + 1 >= events.length) return;
    running = true;
    renderControls();
    try { await show(cursor + 1, true); } finally { running = false; renderControls(); }
  }

  async function rewind() {
    if (running) return;
    auto = false;
    if (cursor > 0) {
      await show(cursor - 1, false);
    } else if (cursor === 0) {
      cursor = -1;
      canvas.clearTokens();
    }
    renderControls();
    renderList();
  }

  async function show(i, animate) {
    cursor = i;
    renderList();
    const ev = events[i];
    await canvas.move(ev, animate ? HOP_MS / SPEEDS[speedIndex] : 0);
    if (onShow) onShow(ev);
  }

  function reset() {
    if (running) auto = false;
    events.length = 0;
    cursor = -1;
    canvas.clearTokens();
    if (onShow) onShow(null);
    renderList();
    renderControls();
  }

  function renderControls() {
    play.textContent = auto ? "Pauză" : "Redă";
    play.setAttribute("aria-pressed", String(auto));
    step.disabled = running || cursor + 1 >= events.length;
    back.disabled = running || cursor < 0;
    clear.disabled = !events.length;
  }

  function renderList() {
    list.innerHTML = events.length
      ? events.map((ev, i) => `<li data-i="${i}" class="${i === cursor ? "current" : i < cursor ? "done" : "pending"} ${ev.kind}">
          <span class="n">${i + 1}</span>
          <span class="t">${ev.at.toLocaleTimeString("ro-RO")}</span>
          <span class="kind">${KIND_NAME[ev.kind] || esc(ev.kind)}</span>
          <span class="from">${esc(ev.fromName || "")}</span>
          <span class="to">${esc(ev.toName || "")}</span>
          <span class="what">${ev.detail || ""}</span>
        </li>`).join("")
      : '<li class="empty">Niciun eveniment încă. Apasă „Deturnează” sau „Trimite pachetul”.</li>';
    const current = list.querySelector("li.current");
    if (current) current.scrollIntoView({ block: "nearest" });
    count.textContent = events.length ? `${Math.max(0, cursor + 1)} din ${events.length}` : "";
  }

  return { enqueue, resume, reset };
}
