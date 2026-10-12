// JSON over the dashboard API. Every POST carries a JSON body, as the server requires.
// A failed request rejects with the server's message, or with the page's own sentence
// when the server refused for load (429) or failed upstream (5xx).
// Paths are absolute from the app's root; BASE is the prefix the app is served under
// (the layout writes it into <html data-base>, empty at the root).

import { t } from "./i18n.js";

const BASE = document.documentElement.dataset.base ?? "";
const TOO_MANY = 429;
const SERVER_ERROR = 500;

export async function get(path) {
  const response = await fetch(BASE + path);
  return unwrap(response);
}

export async function post(path, body) {
  const response = await fetch(BASE + path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body ?? {}),
  });
  return unwrap(response);
}

async function unwrap(response) {
  const data = await response.json().catch(() => ({}));
  if (response.status === TOO_MANY) throw new Error(t("error.rate"));
  if (response.status >= SERVER_ERROR) throw new Error(t("error.server"));
  if (!response.ok) throw new Error(data.error || data.detail || `HTTP ${response.status}`);
  return data;
}

// Calls `tick` now and every `ms`, skipping a tick while the previous one still runs.
export function every(ms, tick) {
  let busy = false;
  const run = async () => {
    if (busy) return;
    busy = true;
    try { await tick(); } finally { busy = false; }
  };
  run();
  return setInterval(run, ms);
}
