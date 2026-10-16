// The simulator's event queue and its playback. Every hop of a message, a packet or a
// kernel answer is one event between two nodes, listed in order with the moment it
// happened. The transport plays the queue at the chosen speed, one step at a time, or
// back to an earlier event; while it plays, events that arrive run as they come; while
// it waits, the slot beside the controls counts the events that wait.

import { esc } from "./format.js";
import { LOCALE, count, t } from "./i18n.js";

export const SPEEDS = [0.25, 0.5, 1, 2, 4];
const HOP_MS = 1400;
const GAP_SHOWN_MS = 10000;
const KIND_NAME = { update: "UPDATE", withdraw: t("kind.withdraw"), packet: t("token.packet"), kernel: t("token.kernel") };

export function createTransport({ canvas, list, play, back, step, clear, speed, speedLabel, count: counter, now, onShow }) {
  const events = [];
  let cursor = -1;
  let auto = false;
  let running = false;
  let stepWaiting = false;
  let speedIndex = SPEEDS.indexOf(1);

  play.addEventListener("click", () => { if (auto && running) pause(); else resume(); });
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

  // Queues events; an event without a time of its own happened now.
  function enqueue(batch) {
    const at = new Date();
    batch.forEach((ev) => events.push({ ...ev, at: ev.at || at }));
    renderList();
    renderControls();
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
      settle();
    }
  }

  // A step asked for while a hop is in flight runs once that hop has landed.
  async function forward() {
    if (running) { stepWaiting = true; return; }
    if (cursor + 1 >= events.length) return;
    running = true;
    renderControls();
    try { await show(cursor + 1, true); } finally { settle(); }
  }

  function settle() {
    running = false;
    renderControls();
    if (stepWaiting) { stepWaiting = false; forward(); } else if (auto && cursor + 1 < events.length) run();
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
    if (onShow) onShow(ev, i, events.length);
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

  // Leaves the events that wait behind, so what a press causes plays next, not after them.
  function skipToEnd() {
    if (cursor + 1 >= events.length) return;
    cursor = events.length - 1;
    canvas.clearTokens();
    if (onShow) onShow(null);
    renderList();
    renderControls();
  }

  function renderControls() {
    const playing = auto && running;
    play.textContent = playing ? t("pause") : t("play");
    play.setAttribute("aria-pressed", String(playing));
    play.disabled = !playing && cursor + 1 >= events.length;
    step.disabled = cursor + 1 >= events.length;
    back.disabled = running || cursor < 0;
    clear.disabled = !events.length;
  }

  function renderList() {
    list.innerHTML = events.length
      ? events.map((ev, i) => `<li data-i="${i}" class="${i === cursor ? "current" : i < cursor ? "done" : "pending"} ${ev.kind}">
          <span class="n">${i + 1}</span>
          <span class="t">${ev.at.toLocaleTimeString(LOCALE)}</span>
          <span class="kind">${KIND_NAME[ev.kind] || esc(ev.kind)}${gap(i)}</span>
          <span class="from">${esc(ev.fromName || "")}</span>
          <span class="to">${esc(ev.toName || "")}</span>
          <span class="what">${ev.detail || ""}</span>
        </li>`).join("")
      : `<li class="empty">${esc(t("events.empty"))}</li>`;
    const current = list.querySelector("li.current");
    if (current) current.scrollIntoView({ block: "nearest" });
    counter.textContent = events.length || "";
    renderNow();
  }

  // The real time since the event before, between two reported rows (the ones with an
  // id), when it is short enough to mean something.
  function gap(i) {
    if (i === 0 || !events[i].id || !events[i - 1].id) return "";
    const ms = events[i].at - events[i - 1].at;
    return ms > 0 && ms < GAP_SHOWN_MS ? `<span class="gap">+${ms} ms</span>` : "";
  }

  // The event under the cursor, beside the transport, or how many events wait there;
  // action messages use the same slot.
  function renderNow() {
    const ev = events[cursor];
    now.classList.toggle("event", Boolean(ev));
    if (ev) {
      now.innerHTML = `<span class="n">${cursor + 1} / ${events.length}</span> <span class="kind">${KIND_NAME[ev.kind] || esc(ev.kind)}</span> ${esc(ev.fromName || "")}${ev.toName ? ` &rarr; ${esc(ev.toName)}` : ""}`;
      return;
    }
    const waiting = events.length - cursor - 1;
    now.textContent = waiting > 0 ? count(waiting, "events.waiting") : "";
  }

  function pause() {
    auto = false;
    renderControls();
  }

  function current() {
    return cursor < 0 ? null : { ev: events[cursor], index: cursor, total: events.length };
  }

  return { enqueue, resume, reset, pause, current, skipToEnd };
}
