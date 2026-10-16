// The simulator's events from the route log. A row of route_updates is one change a lab
// router reported over BMP: what it now holds for a prefix, or that the route is gone.
// The Loc-RIB reports a path with the router's own AS in front; without it, the path
// reads as the lab snapshot lists it: empty for the router's own announcement, else
// starting with the AS that sent the route. The hop behind the row runs from that sender
// to the router that stored the route; an own announcement travels nowhere; a withdrawal
// travels from the sender of the route it removes and carries that route's path. Every
// row is also a report from the router to the collector. A row that repeats what the
// router already held, as a table re-sent after a BMP reconnect does, yields nothing.
// No DOM and no texts here: the simulator adds the words, so this module also runs
// under node (tests/js).

export function createLog() {
  const held = new Map();
  let cursor = 0;

  // The hops of the rows, in order; a row at or before the cursor is skipped.
  function take(rows, routers) {
    const byAddress = new Map(routers.map((r) => [r.address, r]));
    const byAsn = new Map(routers.map((r) => [r.asn, r]));
    const hops = [];
    for (const row of rows) {
      if (row.id <= cursor) continue;
      cursor = row.id;
      const router = byAddress.get(row.peer);
      if (!router) continue;
      const key = `${row.peer}|${row.prefix}`;
      const before = held.get(key) || null;
      const after = { originAs: row.origin_as, asPath: row.as_path, withdrawn: row.withdrawn };
      if (before && same(before, after)) continue;
      held.set(key, after);
      const stored = row.withdrawn ? (before ? before.asPath : []) : row.as_path;
      const path = stored[0] === router.asn ? stored.slice(1) : stored;
      const sender = path.length ? byAsn.get(path[0]) || null : null;
      const base = { kind: row.withdrawn ? "withdraw" : "update", router: router.role, prefix: row.prefix, path, at: new Date(row.seen_at), id: row.id };
      hops.push(sender ? { ...base, from: sender.role, to: router.role } : { ...base, from: router.role, to: null });
      hops.push({ ...base, from: router.role, to: "collector" });
    }
    return hops;
  }

  return { take, get cursor() { return cursor; } };
}

function same(a, b) {
  return a.originAs === b.originAs && a.withdrawn === b.withdrawn
    && a.asPath.length === b.asPath.length && a.asPath.every((asn, i) => asn === b.asPath[i]);
}
