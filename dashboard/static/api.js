// JSON over the dashboard API. A failed request rejects with the server's message.

export async function get(path) {
  const response = await fetch(path);
  return unwrap(response);
}

export async function post(path, body) {
  const response = await fetch(path, {
    method: "POST",
    headers: body ? { "Content-Type": "application/json" } : {},
    body: body ? JSON.stringify(body) : undefined,
  });
  return unwrap(response);
}

async function unwrap(response) {
  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(data.error || data.detail || `HTTP ${response.status}`);
  }
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
