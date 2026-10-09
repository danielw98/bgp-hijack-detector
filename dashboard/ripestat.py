"""Thin adapter over the RIPEstat Data API: which prefix covers an address, who
originates it, who holds that AS, and what RPKI says about the pair. Results are
cached for ten minutes. The fetcher is injected so the adapter is tested offline."""

from __future__ import annotations

import json
import time
import urllib.parse
import urllib.request
from collections.abc import Callable
from ipaddress import ip_address, ip_network

BASE = "https://stat.ripe.net/data/{call}/data.json"
SOURCEAPP = "ptirc-bgpwatch"
TTL_SECONDS = 600


class RipeStatError(Exception):
    """RIPEstat did not answer, or answered without the expected data."""


def fetch_json(url: str) -> dict:
    request = urllib.request.Request(url, headers={"User-Agent": SOURCEAPP})
    with urllib.request.urlopen(request, timeout=15) as response:
        return json.load(response)


class RipeStat:
    def __init__(
        self,
        fetch: Callable[[str], dict] = fetch_json,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        self._fetch = fetch
        self._clock = clock
        self._cache: dict[str, tuple[float, dict]] = {}

    def lookup(self, resource: str) -> dict:
        """The announced prefix covering an address or prefix, its origin ASes with
        their holders, and the RPKI validity of each (prefix, origin) pair."""
        resource = normalize(resource)
        info = self._data("network-info", resource=resource)
        prefix = info.get("prefix") or ""
        if not prefix:
            return {"resource": resource, "prefix": None, "announced": False, "origins": []}
        overview = self._data("prefix-overview", resource=prefix)
        holders = {str(entry["asn"]): entry.get("holder", "") for entry in overview.get("asns", [])}
        origins = [
            {
                "asn": int(asn),
                "holder": holders.get(str(asn), ""),
                "rpki": self._rpki(int(asn), prefix),
            }
            for asn in info.get("asns", [])
        ]
        return {
            "resource": resource,
            "prefix": prefix,
            "announced": bool(overview.get("announced", bool(origins))),
            "origins": origins,
        }

    def my_ip(self) -> str:
        """The public address RIPEstat sees this process coming from."""
        return self._data("whats-my-ip")["ip"]

    def _rpki(self, asn: int, prefix: str) -> dict:
        data = self._data("rpki-validation", resource=f"AS{asn}", prefix=prefix)
        return {
            "status": data.get("status", "unknown"),
            "roas": [
                {
                    "prefix": roa.get("prefix"),
                    "max_length": roa.get("max_length"),
                    "origin": int(roa["origin"]) if roa.get("origin") else None,
                    "validity": roa.get("validity"),
                }
                for roa in data.get("validating_roas", [])
            ],
        }

    def _data(self, call: str, **params: str) -> dict:
        query = urllib.parse.urlencode({**params, "sourceapp": SOURCEAPP})
        url = f"{BASE.format(call=call)}?{query}"
        cached = self._cache.get(url)
        now = self._clock()
        if cached and now - cached[0] < TTL_SECONDS:
            return cached[1]
        try:
            document = self._fetch(url)
        except OSError as exc:
            raise RipeStatError(f"RIPEstat {call}: {exc}") from exc
        if document.get("status") != "ok" or "data" not in document:
            raise RipeStatError(f"RIPEstat {call}: {document.get('status', 'no status')}")
        self._cache[url] = (now, document["data"])
        return document["data"]


def normalize(resource: str) -> str:
    """An IP address or a CIDR prefix in canonical form; anything else is refused."""
    text = resource.strip()
    try:
        return str(ip_network(text, strict=False)) if "/" in text else str(ip_address(text))
    except ValueError as exc:
        raise ValueError(f"not an IP address or prefix: {text!r}") from exc
