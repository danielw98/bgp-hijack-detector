// Figure 6: the watched prefixes on the real Internet, through RIPE RIS Live. The feed
// strip, one row per watched prefix with the origins its vantage points hold now, the
// open incidents and the last alerts, and the edits of the watchlist: allowed from a
// private address, or with the watch token kept for the session.

import { del, every, get, post, setToken } from "./api.js";
import { ALERT_NAME, esc, fmtTime, shortName } from "./format.js";
import { count, t } from "./i18n.js";
import { feedState, parseOrigins } from "./monitor-data.js";

const $ = (id) => document.getElementById(id);
const LIST_LIMIT = 50;
let data = null;

export function startMonitor() {
  $("monitor-add").addEventListener("submit", (e) => { e.preventDefault(); add(); });
  $("monitor-prefix").addEventListener("change", prefill);
  $("monitor-asn-form").addEventListener("submit", (e) => { e.preventDefault(); addAs(); });
  $("btn-monitor-test").addEventListener("click", test);
  $("monitor-token-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    setToken($("monitor-token").value.trim());
    $("monitor-token").value = "";
    await refresh();
    if (data && !data.editable) {
      setToken("");
      say(t("monitor.token.refused"), true);
    }
  });
  $("monitor-table").addEventListener("click", (e) => {
    const button = e.target.closest("button[data-remove], button[data-expect]");
    if (!button) return;
    if (button.dataset.remove) remove(button.dataset.remove);
    else expect(button.dataset.expect, Number(button.dataset.asn));
  });
  every(5000, refresh);
}

async function refresh() {
  try {
    const [state, alerts] = await Promise.all([get("/api/watch"), get("/api/alerts?source=ris&limit=50")]);
    data = state;
    render(alerts);
    $("raw-monitor").textContent = JSON.stringify(state, null, 2);
  } catch (e) {
    $("monitor-feed").className = "verdict";
    $("monitor-feed").textContent = t("error", { message: e.message });
  }
}

function render(alerts) {
  renderFeed();
  renderTable();
  renderForms();
  renderIncidents();
  renderAlerts(alerts);
}

function renderFeed() {
  const box = $("monitor-feed");
  const state = feedState(data);
  const feed = data.feed;
  const facts = [];
  if (data.rpki.refreshed_at) facts.push(t("monitor.rpki", { n: data.rpki.vrps, time: fmtTime(data.rpki.refreshed_at) }));
  if (data.seeded_at) facts.push(t("monitor.seeded", { time: fmtTime(data.seeded_at) }));
  const live = feed.last_message_at
    ? t("monitor.feed.live", { since: fmtTime(feed.since), messages: feed.messages, last: fmtTime(feed.last_message_at) })
    : t("monitor.feed.live.quiet", { since: fmtTime(feed.since) });
  const text = state === "live" ? live
    : state === "idle" ? t("monitor.feed.idle") : t("monitor.feed.down", { reason: feed.last_error || t("monitor.feed.waiting") });
  box.className = `verdict ${state}`;
  box.innerHTML = `<span class="badge">${t(`monitor.feed.badge.${state}`)}</span><span class="label">${esc(text)}</span><span class="counts">${esc(facts.join(", "))}</span>`;
}

function originChip(prefix, origin) {
  const asn = origin.asn === null ? t("none") : `AS${origin.asn}`;
  const peers = count(origin.peers, "monitor.peer");
  const more = origin.prefixes.length === 1 && origin.prefixes[0] === prefix ? "" : ` (${origin.prefixes.map(esc).join(", ")})`;
  const chip = `<span class="state ${origin.expected ? "seen" : "wrong"}">${esc(asn)}, ${esc(peers)}${more}</span>`;
  const fix = !origin.expected && origin.asn !== null && data.editable
    ? ` <button type="button" class="btn outline small" data-expect="${esc(prefix)}" data-asn="${origin.asn}">${t("monitor.expect", { asn: origin.asn })}</button>`
    : "";
  return chip + fix;
}

function renderTable() {
  const rows = data.prefixes.map((p) => {
    const expected = p.expected_origins.length ? p.expected_origins.map((a) => `AS${a}`).join(", ") : t("none");
    const seen = p.origins.length ? p.origins.map((o) => originChip(p.prefix, o)).join(" ") : `<span class="assumed">${esc(unseen(p))}</span>`;
    const actions = data.editable ? `<button type="button" class="btn outline red small" data-remove="${esc(p.prefix)}">${t("monitor.remove")}</button>` : "";
    return `<tr class="${p.status === "alert" ? "bad" : p.status === "ok" ? "good" : ""}">
      <td><span class="mono">${esc(p.prefix)}</span>${p.description ? `<span class="note">${esc(p.description)}</span>` : ""}</td>
      <td class="mono">${esc(expected)}</td>
      <td><span class="origins">${seen}</span></td>
      <td class="time">${p.last_update ? esc(fmtTime(p.last_update)) : ""}</td>
      <td><span class="badge ${p.status}">${t(`monitor.status.${p.status}`)}</span></td>
      <td>${actions}</td>
    </tr>`;
  }).join("");
  $("monitor-table").innerHTML = rows
    ? `<thead><tr><th>${t("col.prefix")}</th><th>${t("monitor.col.expected")}</th><th>${t("monitor.col.seen")}</th><th>${t("monitor.col.last")}</th><th>${t("monitor.col.status")}</th><th></th></tr></thead><tbody>${rows}</tbody>`
    : `<tbody><tr><td class="empty">${t("monitor.empty")}</td></tr></tbody>`;
}

// Why a row shows no origin: the seed is still running, it failed, or nothing was heard yet.
function unseen(p) {
  if (p.seed_failure) return t("monitor.seed.failed", { reason: p.seed_failure });
  return data.seeding ? t("monitor.seeding") : t("monitor.seen.none");
}

function renderForms() {
  $("monitor-add").hidden = !data.editable;
  $("monitor-asn-form").hidden = !data.editable;
  $("monitor-token-form").hidden = data.editable || !data.token_required;
  if (!data.editable && !$("monitor-msg").textContent) $("monitor-msg").textContent = data.token_required ? t("monitor.locked") : t("monitor.readonly");
}

function renderIncidents() {
  const list = data.incidents;
  $("monitor-incidents-summary").textContent = t("monitor.incidents.count", { n: list.length });
  const rows = list.map((i) => `
    <tr class="bad">
      <td class="time">${esc(fmtTime(i.first_seen))}</td>
      <td class="mono">${esc(i.prefix)}</td>
      <td class="mono">${i.origin === null ? esc(t("none")) : `AS${esc(i.origin)}`}</td>
      <td class="mono">${i.expected_origins.map((a) => `AS${a}`).join(", ")}</td>
      <td>${count(i.peers.length, "monitor.peer")}, ${count(i.alerts, "monitor.alert")}</td>
      <td>${i.alert_types.map((type) => esc(ALERT_NAME[type] || type)).join(", ")}</td>
    </tr>`).join("");
  $("monitor-incidents").innerHTML = rows
    ? `<table class="data"><thead><tr><th>${t("monitor.col.since")}</th><th>${t("col.prefix")}</th><th>${t("col.seen.origin")}</th><th>${t("monitor.col.expected")}</th><th>${t("monitor.col.seen")}</th><th>${t("col.type")}</th></tr></thead><tbody>${rows}</tbody></table>`
    : `<div class="empty">${t("monitor.incidents.none")}</div>`;
}

function renderAlerts(alerts) {
  $("monitor-alerts-summary").textContent = t("monitor.alerts.count", { n: alerts.length });
  const rows = alerts.slice(0, LIST_LIMIT).map((a) => `
    <tr>
      <td class="time">${esc(fmtTime(a.raised_at))}</td>
      <td>${esc(ALERT_NAME[a.alert_type] || a.alert_type)}</td>
      <td class="mono">${esc(a.prefix)}</td>
      <td class="mono">${esc(a.peer)}</td>
      <td class="mono">AS${esc(a.observed_origin)}</td>
    </tr>`).join("");
  $("monitor-alerts").innerHTML = rows
    ? `<table class="data"><thead><tr><th>${t("col.when")}</th><th>${t("col.type")}</th><th>${t("col.prefix")}</th><th>${t("col.peer")}</th><th>${t("col.seen.origin")}</th></tr></thead><tbody>${rows}</tbody></table>`
    : `<div class="empty">${t("monitor.alerts.none")}</div>`;
}

function say(text, bad = false) {
  const box = $("monitor-msg");
  box.textContent = text;
  box.className = bad ? "msg bad" : "msg";
}

// The expected origins and the holder come from RIPEstat when the fields are still empty.
async function prefill() {
  const prefix = $("monitor-prefix").value.trim();
  if (!prefix) return;
  try {
    const seen = await get(`/api/lookup?resource=${encodeURIComponent(prefix)}`);
    if (!seen.origins.length) { say(t("monitor.prefill.none", { prefix })); return; }
    const asns = seen.origins.map((o) => `AS${o.asn}`).join(", ");
    if (!$("monitor-origins").value.trim()) $("monitor-origins").value = asns;
    if (!$("monitor-description").value.trim()) $("monitor-description").value = shortName(seen.origins[0].holder);
    say(t("monitor.prefill", { prefix: seen.prefix, asns }));
  } catch (e) {
    say(t("error", { message: e.message }), true);
  }
}

async function add() {
  let origins;
  try {
    origins = parseOrigins($("monitor-origins").value);
  } catch (e) {
    say(t("monitor.origins.bad", { text: e.message }), true);
    return;
  }
  await act(async () => {
    const result = await post("/api/watch/prefixes", { prefix: $("monitor-prefix").value.trim(), expected_origins: origins, description: $("monitor-description").value.trim() });
    $("monitor-prefix").value = "";
    $("monitor-origins").value = "";
    $("monitor-description").value = "";
    return t("monitor.added", { prefix: result.prefix });
  });
}

async function addAs() {
  let asn;
  try {
    [asn] = parseOrigins($("monitor-asn").value);
  } catch (e) {
    say(t("monitor.origins.bad", { text: e.message }), true);
    return;
  }
  if (asn === undefined) return;
  await act(async () => {
    const result = await post("/api/watch/asn", { asn, description: `AS${asn}` });
    $("monitor-asn").value = "";
    return t("monitor.asn.added", { n: result.added, asn });
  });
}

async function remove(prefix) {
  await act(async () => {
    await del(`/api/watch/prefixes/${prefix}`);
    return t("monitor.removed", { prefix });
  });
}

async function expect(prefix, asn) {
  const entry = data.prefixes.find((p) => p.prefix === prefix);
  await act(async () => {
    await post("/api/watch/prefixes", { prefix, expected_origins: [...entry.expected_origins, asn], description: entry.description });
    return t("monitor.expected", { prefix, asn });
  });
}

async function test() {
  await act(async () => {
    const result = await post("/api/watch/test", {});
    if (!result.webhooks.length) return t("monitor.test.none");
    return result.failed.length ? t("monitor.test.failed", { names: result.failed.join(", ") }) : t("monitor.test.ok", { n: result.webhooks.length });
  });
}

// One edit at a time: the buttons wait, the answer lands in the message line, the table refreshes.
async function act(run) {
  const buttons = [...document.querySelectorAll(".monitor .btn")];
  buttons.forEach((b) => { b.disabled = true; });
  try {
    say(await run());
    await refresh();
  } catch (e) {
    say(t("error", { message: e.message }), true);
  } finally {
    buttons.forEach((b) => { b.disabled = false; });
  }
}
