// The pure facts of the Monitor figure, tested under node: the origins typed into the
// add row, and the state of the feed as the page words it.

// "3333, AS64496 64496" -> [3333, 64496]; anything that is not an AS number throws.
export function parseOrigins(text) {
  const parts = String(text || "").split(/[\s,;]+/).filter(Boolean);
  const origins = parts.map((part) => {
    const digits = part.replace(/^as/i, "");
    if (!/^\d{1,10}$/.test(digits)) throw new Error(part);
    return Number(digits);
  });
  return [...new Set(origins)];
}

// "live" while the socket is open, "idle" with nothing to watch, "down" otherwise.
export function feedState(data) {
  if (data.feed.connected) return "live";
  if (!data.prefixes.length) return "idle";
  return "down";
}
