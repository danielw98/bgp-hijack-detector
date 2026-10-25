// The main view: the story of a route hijack in five steps, told on a still map of the lab
// and one panel of text. The facts come from a cycle recorded on the real routers; the
// last step can run the attack on them again. Step N opens from the hash "#step-N".

import { get, post } from "./api.js";
import { LOCALE, t } from "./i18n.js";
import { esc } from "./format.js";
import { TARGET, TOTAL, reach, storySteps } from "./story-data.js";

const CYCLE = "/static/story/cycle.json";
const ROUTERS = ["victim", "transit", "attacker"];
const ASN = { victim: 65001, transit: 65000, attacker: 65003 };
const TALL_BELOW = 760;          // container width under which the routers stack
const SETTLE_MS = 4000;          // the routers converge after a reset
const REPORT_MS = 3500;          // the routers report within a poll or two

// Wide: the routers in a row, the detector under the provider. Tall: one column.
const WIDE = {
  size: [1000, 460],
  box: { victim: [20, 20, 260, 215], transit: [370, 20, 260, 215], attacker: [720, 20, 260, 215], detector: [370, 320, 260, 120] },
  lane: { announce: 80, packet: 150 },
  feed: {
    victim: "M 150 235 L 150 380 L 370 380",
    transit: "M 500 235 L 500 320",
    attacker: "M 850 235 L 850 380 L 630 380",
  },
};
const TALL = {
  size: [420, 1015],
  box: { victim: [60, 20, 340, 215], transit: [60, 305, 340, 215], attacker: [60, 590, 340, 215], detector: [60, 875, 340, 120] },
  lane: { announce: 110, packet: 250 },
  feed: {
    victim: "M 60 127 L 30 127 L 30 935 L 60 935",
    transit: "M 60 412 L 30 412",
    attacker: "M 60 697 L 30 697",
  },
};

export async function mountStory(root) {
  const map = root.querySelector("[data-story=map]");
  const panel = root.querySelector("[data-story=panel]");
  let cycle;
  try {
    cycle = await get(CYCLE);
  } catch (e) {
    panel.innerHTML = `<p class="result bad">${esc(t("error", { message: e.message }))}</p>`;
    return;
  }
  const steps = storySteps(cycle);
  let current = stepFromHash();

  function show(n) {
    current = Math.min(TOTAL, Math.max(1, n));
    if (location.hash !== `#step-${current}`) history.replaceState(null, "", `#step-${current}`);
    draw();
    panel.innerHTML = panelMarkup(steps[current - 1], cycle.recorded_at);
    if (current === TOTAL) wireLive(panel);
  }

  function draw() {
    const layout = map.clientWidth < TALL_BELOW ? TALL : WIDE;
    map.innerHTML = mapMarkup(layout, steps[current - 1]);
  }

  panel.addEventListener("click", (e) => {
    const go = e.target.closest("[data-go]");
    if (go) show(Number(go.dataset.go));
  });
  window.addEventListener("hashchange", () => { if (location.hash.startsWith("#step-")) show(stepFromHash()); });
  new ResizeObserver(draw).observe(map);
  show(current);
}

function stepFromHash() {
  const n = Number(location.hash.replace("#step-", ""));
  return Number.isInteger(n) && n >= 1 && n <= TOTAL ? n : 1;
}

// ---- the panel -------------------------------------------------------------------------

function panelMarkup(step, recordedAt) {
  const n = step.n;
  const dots = Array.from({ length: TOTAL }, (_, i) =>
    `<li><button type="button" data-go="${i + 1}" class="${i + 1 === n ? "current" : ""}" aria-label="${esc(t("story.of", { n: i + 1, total: TOTAL }))}"${i + 1 === n ? ' aria-current="step"' : ""}>${i + 1}</button></li>`).join("");
  const back = n > 1 ? `<button type="button" class="btn outline" data-go="${n - 1}">${esc(t("story.back"))}</button>` : "";
  const next = n < TOTAL ? `<button type="button" class="btn" data-go="${n + 1}">${esc(t("story.next"))}</button>` : "";
  const date = new Date(recordedAt).toLocaleDateString(LOCALE);
  return `<header><span class="of">${esc(t("story.of", { n, total: TOTAL }))}</span><ol class="dots">${dots}</ol></header>
    <h2>${esc(t(`story.s${n}.title`))}</h2>
    <p class="text">${esc(t(`story.s${n}.text`))}</p>
    ${resultMarkup(step)}
    <nav class="steps">${back}${next}</nav>
    ${n === TOTAL ? liveMarkup() : ""}
    <p class="note">${esc(t("story.recorded", { date }))}</p>`;
}

function resultMarkup(step) {
  if (step.alert) {
    return `<p class="result bad">${esc(t("story.alert", { seconds: fmtSeconds(step.alert.seconds) }))}</p>`;
  }
  if (step.packet) {
    const refused = step.rejected && step.rejected.length ? " " + t("story.refused") : "";
    return `<p class="result ${step.packet.ok ? "ok" : "bad"}">${esc(reachText(step.packet) + refused)}</p>`;
  }
  return `<p class="result ok">${esc(t("story.s1.result"))}</p>`;
}

function reachText(packet, prefix = "story.reach") {
  return t(`${prefix}.${packet.at === "victim" || packet.at === "attacker" ? packet.at : "none"}`);
}

function fmtSeconds(seconds) {
  return seconds.toLocaleString(LOCALE, { maximumFractionDigits: 1, minimumFractionDigits: 1 });
}

function liveMarkup() {
  return `<div class="live"><button type="button" class="btn red" data-live>${esc(t("story.live.run"))}</button><p class="live-msg" aria-live="polite"></p></div>`;
}

// The attack once more on the real routers: from rest, announce the half block, read the
// alerts and where the packet goes, then withdraw.
function wireLive(panel) {
  const button = panel.querySelector("[data-live]");
  const msg = panel.querySelector(".live-msg");
  button.addEventListener("click", async () => {
    button.disabled = true;
    msg.className = "live-msg";
    msg.textContent = t("story.live.running");
    try {
      await post("/api/lab/rov", { on: false });
      await post("/api/lab/reset");
      await wait(SETTLE_MS);
      const started = await post("/api/lab/trigger", { scenario: "subprefix" });
      await wait(REPORT_MS);
      const issued = Date.parse(started.issued_at);
      const alerts = (await get("/api/alerts?limit=50")).filter((a) => a.source === "lab" && Date.parse(a.raised_at) >= issued);
      const packet = reach(await post("/api/lab/trace", { from: "transit", to: TARGET }));
      await post("/api/lab/reset");
      const first = alerts.length ? Math.min(...alerts.map((a) => Date.parse(a.raised_at))) : null;
      const alarm = first === null
        ? t("story.live.quiet", { seconds: fmtSeconds(REPORT_MS / 1000) })
        : t("story.live.alert", { seconds: fmtSeconds((first - issued) / 1000) });
      msg.textContent = `${alarm} ${reachText(packet, "story.live.reach")} ${t("story.live.withdrawn")}`;
    } catch (e) {
      msg.className = "live-msg bad";
      msg.textContent = t("story.live.unavailable", { message: e.message });
    } finally {
      button.disabled = false;
    }
  });
}

const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// ---- the map ---------------------------------------------------------------------------

function mapMarkup(layout, step) {
  const [W, H] = layout.size;
  const feeds = ROUTERS.map((role) => `<path class="feed" d="${layout.feed[role]}"></path>`).join("");
  return `<svg class="story-map ${layout === TALL ? "tall" : "wide"}" viewBox="0 0 ${W} ${H}" role="img" aria-label="${esc(t("story.map.label"))}">
    <defs><marker id="head" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse"><path d="M 0 0 L 10 5 L 0 10 z"></path></marker></defs>
    <g class="feeds">${feeds}</g>
    ${ROUTERS.map((role) => routerBox(layout, role, step)).join("")}
    ${detectorBox(layout, step)}
    ${arrows(layout, step)}
  </svg>`;
}

function routerBox(layout, role, step) {
  const [x, y, w, h] = layout.box[role];
  const lines = boxLines(role, step);
  const arrives = step.packet && step.packet.at === role;
  const pill = arrives
    ? `<rect class="pill ${step.packet.ok ? "ok" : "bad"}" x="${x + 16}" y="${y + h - 52}" width="${w - 32}" height="38" rx="19"></rect>
       <text class="pill-text" x="${x + w / 2}" y="${y + h - 26}" text-anchor="middle">${esc(t("story.map.arrives"))}</text>`
    : "";
  return `<g class="box ${role}">
    <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="12"></rect>
    <text class="name" x="${x + 20}" y="${y + 42}">${esc(t(`story.name.${role}`))}</text>
    <text class="sub" x="${x + 20}" y="${y + 70}">${esc(t("story.map.network", { asn: ASN[role] }))}</text>
    ${lines.map((line, i) => `<text class="line ${line.tone}" x="${x + 20}" y="${y + 106 + i * 26}">${esc(line.text)}</text>`).join("")}
    ${pill}
  </g>`;
}

// What a router says about itself at this step, at most two lines.
function boxLines(role, step) {
  if (role === "victim") return [{ text: t("story.map.owns"), tone: "" }, { text: "203.0.113.0/24", tone: "mono" }];
  if (role === "attacker" && step.announce.by === "attacker") return [{ text: t("story.map.claims"), tone: "bad" }, { text: step.announce.prefix, tone: "mono bad" }];
  if (role === "transit" && step.announce.refused) return [{ text: t("story.map.checks"), tone: "guard" }];
  return [];
}

function detectorBox(layout, step) {
  const [x, y, w, h] = layout.box.detector;
  const alarmed = Boolean(step.alert);
  return `<g class="box detector${alarmed ? " alarmed" : ""}">
    <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="12"></rect>
    <text class="name" x="${x + 20}" y="${y + 42}">${esc(t("story.name.detector"))}</text>
    <text class="line${alarmed ? " bad" : ""}" x="${x + 20}" y="${y + 80}">${esc(alarmed ? t("story.map.alarm") : t("story.map.watches"))}</text>
  </g>`;
}

// The route announcements on one lane, the packet on the other; each arrow joins two
// neighbouring routers and is labelled beside it.
function arrows(layout, step) {
  const drawn = [];
  const announce = step.announce;
  if (announce.by === "victim") drawn.push(["victim", "transit", "announce", "route"], ["transit", "attacker", "announce", "route"]);
  else if (announce.refused) drawn.push(["attacker", "transit", "announce bad refused", "refused"]);
  else drawn.push(["attacker", "transit", "announce bad", "route"], ["transit", "victim", "announce bad", "route"]);
  if (step.packet && step.packet.at) drawn.push(["transit", step.packet.at, `packet ${step.packet.ok ? "ok" : "bad"}`, "packet"]);
  return drawn.map(([from, to, kind, label]) => arrow(layout, from, to, kind, t(`story.map.${label}`))).join("");
}

function arrow(layout, from, to, kind, label) {
  const lane = layout.lane[kind.split(" ")[0]];
  const [ax, ay, aw, ah] = layout.box[from];
  const [bx, by, bw, bh] = layout.box[to];
  const gap = 8;
  if (layout === WIDE) {
    const y = ay + lane;
    const [x1, x2] = ax < bx ? [ax + aw + gap, bx - gap] : [ax - gap, bx + bw + gap];
    const mid = (x1 + x2) / 2;
    return `<g class="arrow ${kind}"><line x1="${x1}" y1="${y}" x2="${x2}" y2="${y}" marker-end="url(#head)"></line>
      <text x="${mid}" y="${y + 30}" text-anchor="middle">${esc(label)}</text></g>`;
  }
  const x = ax + lane;
  const [y1, y2] = ay < by ? [ay + ah + gap, by - gap] : [ay - gap, by + bh + gap];
  const mid = (y1 + y2) / 2;
  return `<g class="arrow ${kind}"><line x1="${x}" y1="${y1}" x2="${x}" y2="${y2}" marker-end="url(#head)"></line>
    <text x="${x + 20}" y="${mid + 6}">${esc(label)}</text></g>`;
}
