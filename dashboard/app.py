"""Dashboard for the BGP hijack detector: live alerts, route history, and an isolated
lab control (simulate / reset the hijack) so a demo needs no typed commands.

The lab control shells into the attacker router over the Docker socket; it is enabled
only when LAB_CONTROL=1 and the socket is mounted. The vtysh command lists mirror
lab/trigger-hijack.sh and lab/stop-hijack.sh.
"""

from __future__ import annotations

import os
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, Query
from fastapi.responses import HTMLResponse, JSONResponse
from psycopg.rows import dict_row
from psycopg_pool import ConnectionPool

DB_URL = os.environ["BGPWATCH_DATABASE_URL"]
LAB_CONTROL = os.environ.get("LAB_CONTROL", "0") == "1"
ATTACKER = os.environ.get("LAB_ATTACKER_CONTAINER", "lab-attacker")
HIJACK_PREFIXES = ["203.0.113.0/24", "203.0.113.0/25"]

TEMPLATES = Path(__file__).parent / "templates"

pool = ConnectionPool(
    DB_URL, min_size=1, max_size=4, open=False, kwargs={"row_factory": dict_row}
)


@asynccontextmanager
async def lifespan(_app: FastAPI):
    pool.open()
    yield
    pool.close()


app = FastAPI(title="BGP hijack detector", lifespan=lifespan)


@app.get("/", response_class=HTMLResponse)
def index() -> str:
    return (TEMPLATES / "index.html").read_text(encoding="utf-8")


@app.get("/api/stats")
def stats() -> dict:
    with pool.connection() as conn:
        by_type = conn.execute(
            "SELECT alert_type, count(*) AS c FROM alerts GROUP BY alert_type"
        ).fetchall()
        totals = conn.execute(
            """
            SELECT (SELECT count(*) FROM alerts)              AS alerts,
                   (SELECT count(*) FROM route_updates)       AS routes,
                   (SELECT count(*) FROM monitored_prefixes)  AS monitored
            """
        ).fetchone()
    return {"by_type": {r["alert_type"]: r["c"] for r in by_type}, **totals}


@app.get("/api/alerts")
def alerts(limit: int = Query(100, le=500)) -> list[dict]:
    with pool.connection() as conn:
        return conn.execute(
            """
            SELECT id, raised_at, alert_type,
                   prefix::text AS prefix, observed_origin, expected_origins,
                   covering_prefix::text AS covering_prefix, peer, as_path, detail
            FROM alerts ORDER BY id DESC LIMIT %s
            """,
            (limit,),
        ).fetchall()


@app.get("/api/routes")
def routes(limit: int = Query(100, le=500)) -> list[dict]:
    with pool.connection() as conn:
        return conn.execute(
            """
            SELECT id, seen_at, peer, prefix::text AS prefix, origin_as,
                   as_path, next_hop::text AS next_hop, withdrawn
            FROM route_updates ORDER BY id DESC LIMIT %s
            """,
            (limit,),
        ).fetchall()


@app.get("/api/monitored")
def monitored() -> list[dict]:
    with pool.connection() as conn:
        return conn.execute(
            """
            SELECT prefix::text AS prefix, expected_origins, description
            FROM monitored_prefixes ORDER BY prefix
            """
        ).fetchall()


@app.post("/api/lab/trigger")
def lab_trigger() -> JSONResponse:
    return _lab_action("trigger")


@app.post("/api/lab/reset")
def lab_reset() -> JSONResponse:
    return _lab_action("reset")


def _vtysh_commands(action: str) -> list[str]:
    if action == "trigger":
        cmds = ["configure terminal"]
        cmds += [f"ip route {p} Null0" for p in HIJACK_PREFIXES]
        cmds += ["router bgp 65003", "address-family ipv4 unicast"]
        cmds += [f"network {p}" for p in HIJACK_PREFIXES]
        cmds.append("end")
        return cmds
    cmds = ["configure terminal", "router bgp 65003", "address-family ipv4 unicast"]
    cmds += [f"no network {p}" for p in HIJACK_PREFIXES]
    cmds += ["exit-address-family", "exit"]
    cmds += [f"no ip route {p} Null0" for p in HIJACK_PREFIXES]
    cmds.append("end")
    return cmds


def _lab_action(action: str) -> JSONResponse:
    if not LAB_CONTROL:
        return JSONResponse({"ok": False, "error": "lab control disabled"}, status_code=403)
    import docker

    command = ["vtysh"]
    for c in _vtysh_commands(action):
        command += ["-c", c]
    try:
        container = docker.from_env().containers.get(ATTACKER)
        result = container.exec_run(command)
    except Exception as exc:  # docker errors are reported to the operator, not raised
        return JSONResponse({"ok": False, "error": str(exc)}, status_code=502)
    # vtysh returns non-zero for benign cases (removing absent config, saving to the
    # read-only mounted frr.conf), so success is "the command ran": the live route and
    # alert tables are the ground truth. The exit code is still reported for inspection.
    output = result.output.decode(errors="replace")
    return JSONResponse(
        {"ok": True, "action": action, "exit_code": result.exit_code, "output": output[-600:]}
    )
