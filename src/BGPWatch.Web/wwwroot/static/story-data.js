// The facts each step of the story shows, read from one cycle recorded on the lab's real
// routers (static/story/cycle.json, written by scripts/record_story.py). Pure: no DOM, no i18n.

export const TOTAL = 5;
export const TARGET = "203.0.113.1";

// Where a packet sent from the provider to TARGET ended up, and whether that is the victim.
export function reach(trace) {
  const hops = (trace && trace.hops) || [];
  const last = hops.length ? hops[hops.length - 1].router : null;
  return { at: last, ok: last === "victim" && trace.outcome === "delivered" };
}

// The first sub-prefix alert after the attacker's announcement: how long the detector took.
export function firstAlert(step) {
  const alerts = (step.alerts || []).filter((a) => a.alert_type === "subprefix_hijack");
  if (!alerts.length) return null;
  const first = alerts.reduce((a, b) => (a.raised_at < b.raised_at ? a : b));
  return {
    seconds: (Date.parse(first.raised_at) - Date.parse(step.started_at)) / 1000,
    prefix: first.prefix,
    observed: first.observed_origin,
    expected: first.expected_origins,
  };
}

// The prefixes the provider refused from the attacker while it validates origins.
export function rejected(step) {
  const provider = step.after.routers.find((r) => r.role === "transit");
  return ((provider && provider.rejected) || []).map((r) => r.prefix);
}

const fromProvider = (step) => reach(step.trace && step.trace.transit && step.trace.transit[TARGET]);

// The five steps as plain facts: what the map draws and what the panel's result line says.
export function storySteps(cycle) {
  const s = cycle.steps;
  return [
    { n: 1, announce: { by: "victim", prefix: "203.0.113.0/24" } },
    { n: 2, announce: { by: "victim", prefix: "203.0.113.0/24" }, packet: fromProvider(s["3"]) },
    { n: 3, announce: { by: "attacker", prefix: "203.0.113.0/25" }, packet: fromProvider(s["5"]) },
    { n: 4, announce: { by: "attacker", prefix: "203.0.113.0/25" }, packet: fromProvider(s["5"]), alert: firstAlert(s["5"]) },
    { n: 5, announce: { by: "attacker", prefix: "203.0.113.0/25", refused: true }, packet: fromProvider(s["8"]), rejected: rejected(s["8"]) },
  ];
}
