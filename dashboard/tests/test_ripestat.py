"""The RIPEstat adapter, against canned answers shaped like the live API gave on
2026-10-09 for 193.0.0.0/21 (RIPE NCC, AS3333)."""

from __future__ import annotations

import sys
from pathlib import Path
from urllib.parse import parse_qs, urlparse

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import ripestat  # noqa: E402

ANSWERS = {
    "network-info": {"asns": ["3333"], "prefix": "193.0.0.0/21"},
    "prefix-overview": {
        "resource": "193.0.0.0/21",
        "announced": True,
        "asns": [{"asn": 3333, "holder": "RIPE-NCC-AS Reseaux IP Europeens Network Coordination Centre (RIPE NCC)"}],
    },
    "rpki-validation": {
        "resource": "3333",
        "prefix": "193.0.0.0/21",
        "status": "valid",
        "validating_roas": [{"origin": "3333", "prefix": "193.0.0.0/21", "validity": "valid", "max_length": 21}],
    },
    "whats-my-ip": {"ip": "2a02:2f04::1"},
}


class FakeFetch:
    def __init__(self, answers: dict = ANSWERS) -> None:
        self.answers = answers
        self.calls: list[str] = []

    def __call__(self, url: str) -> dict:
        call = urlparse(url).path.split("/")[2]
        self.calls.append(url)
        return {"status": "ok", "data": self.answers[call]}


def test_lookup_reports_prefix_origin_holder_and_rpki() -> None:
    api = ripestat.RipeStat(fetch=FakeFetch())
    result = api.lookup("193.0.6.139")
    assert result["resource"] == "193.0.6.139"
    assert result["prefix"] == "193.0.0.0/21"
    assert result["announced"] is True
    (origin,) = result["origins"]
    assert origin["asn"] == 3333
    assert origin["holder"].startswith("RIPE-NCC-AS")
    assert origin["rpki"]["status"] == "valid"
    assert origin["rpki"]["roas"] == [{"prefix": "193.0.0.0/21", "max_length": 21, "origin": 3333, "validity": "valid"}]


def test_every_call_names_the_source_app_and_the_resource() -> None:
    fetch = FakeFetch()
    ripestat.RipeStat(fetch=fetch).lookup("193.0.0.0/21")
    queries = [parse_qs(urlparse(url).query) for url in fetch.calls]
    assert all(q["sourceapp"] == ["ptirc-bgpwatch"] for q in queries)
    assert queries[0]["resource"] == ["193.0.0.0/21"]
    assert queries[2]["resource"] == ["AS3333"] and queries[2]["prefix"] == ["193.0.0.0/21"]


def test_answers_are_cached_within_the_ttl() -> None:
    fetch = FakeFetch()
    now = [0.0]
    api = ripestat.RipeStat(fetch=fetch, clock=lambda: now[0])
    api.lookup("193.0.6.139")
    api.lookup("193.0.6.139")
    assert len(fetch.calls) == 3
    now[0] = ripestat.TTL_SECONDS + 1
    api.lookup("193.0.6.139")
    assert len(fetch.calls) == 6


def test_an_unannounced_resource_has_no_prefix_and_no_origins() -> None:
    answers = dict(ANSWERS, **{"network-info": {"asns": [], "prefix": ""}})
    result = ripestat.RipeStat(fetch=FakeFetch(answers)).lookup("203.0.113.9")
    assert result == {"resource": "203.0.113.9", "prefix": None, "announced": False, "origins": []}


def test_my_ip_comes_from_whats_my_ip() -> None:
    assert ripestat.RipeStat(fetch=FakeFetch()).my_ip() == "2a02:2f04::1"


def test_a_failed_answer_raises_the_adapter_error() -> None:
    def failing(url: str) -> dict:
        return {"status": "error", "messages": [["error", "nope"]]}

    try:
        ripestat.RipeStat(fetch=failing).lookup("193.0.6.139")
    except ripestat.RipeStatError:
        return
    raise AssertionError("expected RipeStatError")


def test_normalize_accepts_addresses_and_prefixes_only() -> None:
    assert ripestat.normalize(" 193.0.6.139 ") == "193.0.6.139"
    assert ripestat.normalize("193.0.6.139/21") == "193.0.0.0/21"
    try:
        ripestat.normalize("example.org")
    except ValueError:
        return
    raise AssertionError("expected ValueError")
