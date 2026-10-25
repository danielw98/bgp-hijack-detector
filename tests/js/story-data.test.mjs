import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { firstAlert, reach, rejected, storySteps, TOTAL } from "../../src/BGPWatch.Web/wwwroot/static/story-data.js";

const cycle = JSON.parse(readFileSync(new URL("../../src/BGPWatch.Web/wwwroot/static/story/cycle.json", import.meta.url)));
const steps = storySteps(cycle);

test("the story has TOTAL steps numbered from 1", () => {
  assert.deepEqual(steps.map((s) => s.n), [1, 2, 3, 4, 5]);
  assert.equal(steps.length, TOTAL);
});

test("before the attack the packet reaches the victim", () => {
  assert.deepEqual(steps[1].packet, { at: "victim", ok: true });
});

test("during the attack the packet reaches the attacker", () => {
  assert.equal(steps[2].packet.at, "attacker");
  assert.equal(steps[2].packet.ok, false);
});

test("the detector's first sub-prefix alert comes within seconds of the attack", () => {
  const alert = steps[3].alert;
  assert.equal(alert.prefix, "203.0.113.0/25");
  assert.equal(alert.observed, 65003);
  assert.ok(alert.seconds > 0 && alert.seconds < 5, `took ${alert.seconds} s`);
});

test("with validation on, the packet reaches the victim and the provider refused routes", () => {
  assert.deepEqual(steps[4].packet, { at: "victim", ok: true });
  assert.ok(steps[4].rejected.length > 0);
});

test("a packet delivered anywhere but the victim is not ok", () => {
  const trace = { outcome: "delivered", hops: [{ router: "transit" }, { router: "attacker" }] };
  assert.deepEqual(reach(trace), { at: "attacker", ok: false });
});

test("a missing trace reaches nowhere", () => {
  assert.deepEqual(reach(undefined), { at: null, ok: false });
});

test("no sub-prefix alert gives no first alert", () => {
  const step = { started_at: "2026-10-16T17:51:38Z", alerts: [{ alert_type: "origin_hijack", raised_at: "2026-10-16T17:51:39Z" }] };
  assert.equal(firstAlert(step), null);
});

test("the earliest sub-prefix alert is the first, timed from the step's start", () => {
  const step = {
    started_at: "2026-10-16T17:51:38.000Z",
    alerts: [
      { alert_type: "subprefix_hijack", raised_at: "2026-10-16T17:51:38.900Z", prefix: "p", observed_origin: 1, expected_origins: [2] },
      { alert_type: "subprefix_hijack", raised_at: "2026-10-16T17:51:38.250Z", prefix: "p", observed_origin: 1, expected_origins: [2] },
    ],
  };
  assert.equal(firstAlert(step).seconds, 0.25);
});

test("a provider with no rejected routes refused nothing", () => {
  assert.deepEqual(rejected({ after: { routers: [{ role: "transit", rejected: [] }] } }), []);
});
