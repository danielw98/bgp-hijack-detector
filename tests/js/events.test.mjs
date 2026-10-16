// What one stored row of the route log becomes on the map. The rows carry the paths as
// the Loc-RIB reports them, the router's own AS first. Run: node --test "tests/js/*.test.mjs"
import test from "node:test";
import assert from "node:assert/strict";
import { createLog } from "../../src/BGPWatch.Web/wwwroot/static/events.js";

const ROUTERS = [
  { role: "victim", address: "10.10.0.11", asn: 65001 },
  { role: "transit", address: "10.10.0.10", asn: 65000 },
  { role: "attacker", address: "10.10.0.13", asn: 65003 },
];
let nextId = 1;

function row(peer, prefix, path, extra = {}) {
  return {
    id: nextId++,
    seen_at: "2026-10-15T09:30:42.345Z",
    peer,
    prefix,
    origin_as: path.length ? path[path.length - 1] : null,
    as_path: path,
    next_hop: null,
    withdrawn: false,
    source: "lab",
    ...extra,
  };
}

const withdrawn = (peer, prefix) => row(peer, prefix, [], { withdrawn: true });
const legs = (hops) => hops.map((h) => `${h.kind} ${h.from}>${h.to}`);

test("a route learned from a neighbour is a hop from the sender, then a report, with the path the snapshot shows", () => {
  const hops = createLog().take([row("10.10.0.10", "203.0.113.0/25", [65000, 65003])], ROUTERS);
  assert.deepEqual(legs(hops), ["update attacker>transit", "update transit>collector"]);
  assert.equal(hops[0].prefix, "203.0.113.0/25");
  assert.deepEqual(hops[1].path, [65003]);
});

test("a route two hops away comes from the neighbour that passed it on", () => {
  const hops = createLog().take([row("10.10.0.11", "203.0.113.0/25", [65001, 65000, 65003])], ROUTERS);
  assert.deepEqual(legs(hops), ["update transit>victim", "update victim>collector"]);
  assert.deepEqual(hops[0].path, [65000, 65003]);
});

test("a path holding only the router's own AS is its own announcement, with an empty path", () => {
  const hops = createLog().take([row("10.10.0.13", "203.0.113.0/24", [65003])], ROUTERS);
  assert.deepEqual(legs(hops), ["update attacker>null", "update attacker>collector"]);
  assert.deepEqual(hops[0].path, []);
  assert.equal(hops[0].router, "attacker");
});

test("an empty path is an own announcement too", () => {
  assert.deepEqual(legs(createLog().take([row("10.10.0.13", "203.0.113.0/24", [])], ROUTERS)), ["update attacker>null", "update attacker>collector"]);
});

test("a withdrawal travels from the sender of the route it removes and keeps that path", () => {
  const log = createLog();
  log.take([row("10.10.0.11", "203.0.113.0/25", [65001, 65000, 65003])], ROUTERS);
  const hops = log.take([withdrawn("10.10.0.11", "203.0.113.0/25")], ROUTERS);
  assert.deepEqual(legs(hops), ["withdraw transit>victim", "withdraw victim>collector"]);
  assert.deepEqual(hops[0].path, [65000, 65003]);
});

test("withdrawing an own announcement stays at the router", () => {
  const log = createLog();
  log.take([row("10.10.0.13", "203.0.113.0/25", [65003])], ROUTERS);
  assert.deepEqual(legs(log.take([withdrawn("10.10.0.13", "203.0.113.0/25")], ROUTERS)), ["withdraw attacker>null", "withdraw attacker>collector"]);
});

test("a withdrawal of a route never seen stays at the router", () => {
  assert.deepEqual(legs(createLog().take([withdrawn("10.10.0.13", "203.0.113.0/24")], ROUTERS)), ["withdraw attacker>null", "withdraw attacker>collector"]);
});

test("a row that repeats what the router already holds yields nothing", () => {
  const log = createLog();
  log.take([row("10.10.0.10", "203.0.113.0/24", [65000, 65001])], ROUTERS);
  assert.deepEqual(log.take([row("10.10.0.10", "203.0.113.0/24", [65000, 65001])], ROUTERS), []);
});

test("a change of path for the same prefix is a new hop", () => {
  const log = createLog();
  log.take([row("10.10.0.13", "203.0.113.0/24", [65003, 65000, 65001])], ROUTERS);
  assert.deepEqual(legs(log.take([row("10.10.0.13", "203.0.113.0/24", [65003])], ROUTERS)), ["update attacker>null", "update attacker>collector"]);
});

test("rows at or before the cursor are skipped and the cursor follows the last id", () => {
  const log = createLog();
  const first = row("10.10.0.10", "203.0.113.0/24", [65000, 65001]);
  log.take([first], ROUTERS);
  assert.equal(log.cursor, first.id);
  assert.deepEqual(log.take([first], ROUTERS), []);
});

test("a row from an address that is not a lab router is ignored", () => {
  assert.deepEqual(createLog().take([row("192.0.2.9", "203.0.113.0/24", [64500, 65001])], ROUTERS), []);
});

test("a log seeded with a snapshot skips rows that repeat it and knows what a withdrawal removes", () => {
  const log = createLog();
  log.seed({ routers: [
    { role: "victim", address: "10.10.0.11", asn: 65001, paths: [{ prefix: "203.0.113.0/25", as_path: [65000, 65003], origin_as: 65003, best: true }] },
    { role: "transit", address: "10.10.0.10", asn: 65000, paths: [{ prefix: "203.0.113.0/24", as_path: [65001], origin_as: 65001, best: true }] },
  ] });
  assert.deepEqual(log.take([row("10.10.0.10", "203.0.113.0/24", [65000, 65001])], ROUTERS), []);
  const hops = log.take([withdrawn("10.10.0.11", "203.0.113.0/25")], ROUTERS);
  assert.deepEqual(legs(hops), ["withdraw transit>victim", "withdraw victim>collector"]);
  assert.deepEqual(hops[0].path, [65000, 65003]);
});

test("the hop keeps the row's time and id", () => {
  const first = row("10.10.0.10", "203.0.113.0/24", [65000, 65001]);
  const [hop] = createLog().take([first], ROUTERS);
  assert.equal(hop.at.toISOString(), "2026-10-15T09:30:42.345Z");
  assert.equal(hop.id, first.id);
});
