import { test } from "node:test";
import assert from "node:assert/strict";
import { feedState, parseOrigins } from "../../src/BGPWatch.Web/wwwroot/static/monitor-data.js";

test("origins come as numbers, with or without AS, once each", () => {
  assert.deepEqual(parseOrigins("3333, AS64496 64496; as3333"), [3333, 64496]);
  assert.deepEqual(parseOrigins(""), []);
});

test("a word that is not an AS number is refused by name", () => {
  assert.throws(() => parseOrigins("3333 ripe"), { message: "ripe" });
  assert.throws(() => parseOrigins("AS"), { message: "AS" });
});

test("the feed is live, idle or down", () => {
  assert.equal(feedState({ feed: { connected: true }, prefixes: [] }), "live");
  assert.equal(feedState({ feed: { connected: false }, prefixes: [] }), "idle");
  assert.equal(feedState({ feed: { connected: false }, prefixes: [{}] }), "down");
});
