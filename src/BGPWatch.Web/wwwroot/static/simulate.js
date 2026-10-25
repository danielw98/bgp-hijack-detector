// The packet simulation: at each router, the chosen path with the longest prefix that
// contains the destination decides the next hop, until the packet is delivered to a
// router that holds the address or dropped by a router that originates the prefix
// without holding it (a blackhole). Pure: it reads the lab snapshot only.

const MAX_HOPS = 6;

function parseAddress(text) {
  return text.split(".").reduce((n, octet) => n * 256 + Number(octet), 0);
}

export function prefixLength(prefix) {
  return Number(prefix.split("/")[1]);
}

export function contains(prefix, address) {
  const [base, length] = prefix.split("/");
  const bits = Number(length);
  const mask = bits === 0 ? 0 : (2 ** 32 - 2 ** (32 - bits));
  const net = parseAddress(base);
  const addr = parseAddress(address);
  return Math.floor(net / 2 ** (32 - bits)) === Math.floor(addr / 2 ** (32 - bits)) && mask >= 0;
}

export function simulate(lab, fromRole, destination) {
  const byRole = new Map(lab.routers.map((r) => [r.role, r]));
  const byAddress = new Map(lab.routers.map((r) => [r.address, r]));
  const steps = [];
  let router = byRole.get(fromRole);
  for (let i = 0; i < MAX_HOPS && router; i++) {
    const candidates = router.paths.filter((p) => p.best && contains(p.prefix, destination));
    if (!candidates.length) {
      steps.push({ router: router.role, kind: "unreachable", candidates, chosen: null, next: null });
      return { steps, outcome: "unreachable" };
    }
    const chosen = candidates.reduce((a, b) => (prefixLength(b.prefix) > prefixLength(a.prefix) ? b : a));
    if (chosen.next_hop === null) {
      const held = router.hosts.includes(destination);
      steps.push({ router: router.role, kind: held ? "delivered" : "blackhole", candidates, chosen, next: null });
      return { steps, outcome: held ? "delivered" : "dropped" };
    }
    const next = byAddress.get(chosen.next_hop);
    steps.push({ router: router.role, kind: "forward", candidates, chosen, next: next ? next.role : null, nextHop: chosen.next_hop });
    if (!next) return { steps, outcome: "lost" };
    router = next;
  }
  return { steps, outcome: "loop" };
}
