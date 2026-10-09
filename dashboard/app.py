"""Dashboard for the BGP hijack detector: the API behind the page (incidents grouped
from the alerts, the history of a prefix, the recorded incident with its timeline, a
RIPEstat lookup) and the demo controls over the Docker socket (hijack scenarios, reset,
a real traceroute from a lab router, reload of the recorded incident, clearing the lab's
history), so a demo needs no typed commands.

The lab view and the controls run commands in the lab routers and in the collector
over the Docker socket; they are enabled only when LAB_CONTROL=1 and the socket is
mounted. The vtysh command lists mirror lab/trigger-hijack.sh and lab/stop-hijack.sh.
"""

from __future__ import annotations

import json
import os
import re
import tomllib
from contextlib import asynccontextmanager
from functools import lru_cache
from ipaddress import ip_address, ip_network
from pathlib import Path

from fastapi import Body, FastAPI, HTTPException, Query, Request
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from psycopg.rows import dict_row
from psycopg_pool import ConnectionPool

import history
import incidents
import labview
import ripestat
import trace

DB_URL = os.environ["BGPWATCH_DATABASE_URL"]
LAB_CONTROL = os.environ.get("LAB_CONTROL", "0") == "1"
ATTACKER = os.environ.get("LAB_ATTACKER_CONTAINER", "lab-attacker")
COLLECTOR = os.environ.get("COLLECTOR_CONTAINER", "app-collector")
INCIDENTS_DIR = Path(os.environ.get("INCIDENTS_DIR", "/app/incidents"))
LAB_PREFIX = ip_network("203.0.113.0/24")
SCENARIOS = {
    "exact": ["203.0.113.0/24"],
    "subprefix": ["203.0.113.0/25"],
    "both": ["203.0.113.0/24", "203.0.113.0/25"],
}
INCIDENT = "youtube-2008"
REPLAY_COMMAND = ["python3", "-m", "bgpwatch.replay", "--force", f"incidents/{INCIDENT}/config.toml"]
RESEND_COMMAND = [
    "vtysh",
    "-c",
    f"clear bgp ipv4 unicast {labview.COLLECTOR['address']} soft out",
]
CONTROL_DISABLED = {"ok": False, "error": "lab control disabled"}
ALERT_COLUMNS = """
    id, raised_at, alert_type, prefix::text AS prefix, observed_origin, expected_origins,
    covering_prefix::text AS covering_prefix, peer, as_path, detail, source
"""
ROUTE_COLUMNS = """
    id, seen_at, peer, prefix::text AS prefix, origin_as, as_path,
    next_hop::text AS next_hop, withdrawn, source
"""

TEMPLATES = Path(__file__).parent / "templates"
STATIC = Path(__file__).parent / "static"

pool = ConnectionPool(
    DB_URL, min_size=1, max_size=4, open=False, kwargs={"row_factory": dict_row}
)
ripe = ripestat.RipeStat()


@asynccontextmanager
async def lifespan(_app: FastAPI):
    pool.open()
    yield
    pool.close()


app = FastAPI(title="BGP hijack detector", lifespan=lifespan)
app.mount("/static", StaticFiles(directory=STATIC), name="static")


@app.get("/", response_class=HTMLResponse)
def index() -> str:
    """The application: the live lab first, one view at a time."""
    return (TEMPLATES / "index.html").read_text(encoding="utf-8")


@app.get("/ghid", response_class=HTMLResponse)
def guide() -> str:
    """The guide: the same figures, read from top to bottom with the explanations."""
    return (TEMPLATES / "ghid.html").read_text(encoding="utf-8")


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
            f"SELECT {ALERT_COLUMNS} FROM alerts ORDER BY id DESC LIMIT %s", (limit,)
        ).fetchall()


@app.get("/api/routes")
def routes(limit: int = Query(100, le=500)) -> list[dict]:
    with pool.connection() as conn:
        return conn.execute(
            f"SELECT {ROUTE_COLUMNS} FROM route_updates ORDER BY id DESC LIMIT %s", (limit,)
        ).fetchall()


@app.get("/api/monitored")
def monitored() -> list[dict]:
    with pool.connection() as conn:
        return _monitored(conn)


@app.get("/api/incidents")
def incident_list() -> list[dict]:
    """Alerts grouped into incidents, newest first."""
    with pool.connection() as conn:
        rows = conn.execute(
            f"SELECT {ALERT_COLUMNS} FROM alerts ORDER BY id DESC LIMIT 5000"
        ).fetchall()
        prefixes = [m["prefix"] for m in _monitored(conn)]
    return incidents.group(rows, prefixes)


@app.get("/api/incidents/{name}")
def incident(name: str) -> dict:
    """The recorded incident's ground truth, documented timeline and AS names."""
    folder = _incident_dir(name)
    config = tomllib.loads((folder / "config.toml").read_text(encoding="utf-8"))
    names_path = folder / "as-names.json"
    names = json.loads(names_path.read_text(encoding="utf-8")) if names_path.exists() else {}
    return {
        "name": name,
        "monitored": config.get("monitored", []),
        "events": config.get("events", []),
        "as_names": names.get("names", {}),
        "as_names_fetched": names.get("fetched"),
    }


@app.get("/api/incidents/{name}/updates")
def incident_updates(name: str) -> list[dict]:
    """Every update of the incident that falls inside its monitored prefixes, in time order."""
    folder = _incident_dir(name)
    config = tomllib.loads((folder / "config.toml").read_text(encoding="utf-8"))
    prefixes = [m["prefix"] for m in config.get("monitored", [])]
    with pool.connection() as conn:
        return conn.execute(
            f"""
            SELECT {ROUTE_COLUMNS} FROM route_updates
            WHERE source = %s AND prefix <<= ANY(%s::cidr[])
            ORDER BY seen_at, id
            """,
            (name, prefixes),
        ).fetchall()


@app.get("/api/prefix/{prefix:path}")
def prefix_history(prefix: str) -> dict:
    """The state of a prefix at every vantage point, its changes, and its alerts."""
    try:
        network = str(ip_network(prefix, strict=False))
    except ValueError:
        raise HTTPException(status_code=400, detail="not a prefix")
    with pool.connection() as conn:
        watched = conn.execute(
            """
            SELECT prefix::text AS prefix, expected_origins, description
            FROM monitored_prefixes WHERE prefix = %s
            """,
            (network,),
        ).fetchone()
        rows = conn.execute(
            f"""
            SELECT {ROUTE_COLUMNS} FROM route_updates
            WHERE prefix <<= %s ORDER BY seen_at DESC, id DESC LIMIT 2000
            """,
            (network,),
        ).fetchall()
        raised = conn.execute(
            f"SELECT {ALERT_COLUMNS} FROM alerts WHERE prefix <<= %s ORDER BY id DESC LIMIT 500",
            (network,),
        ).fetchall()
    return {
        "prefix": network,
        "monitored": watched,
        "current": history.current(rows),
        "changes": history.changes(rows)[:500],
        "alerts": raised,
        "truncated": len(rows) == 2000,
    }


@app.get("/api/lookup")
def lookup(resource: str = Query(..., max_length=64)) -> JSONResponse:
    """What the Internet says today about an address or prefix, via RIPEstat."""
    try:
        return JSONResponse(ripe.lookup(resource))
    except ValueError as exc:
        return JSONResponse({"error": str(exc)}, status_code=400)
    except ripestat.RipeStatError as exc:
        return JSONResponse({"error": str(exc)}, status_code=502)


@app.get("/api/lookup/self")
def lookup_self(request: Request) -> JSONResponse:
    """The visitor's public address and its lookup: the browser's address when it is
    public, else the address RIPEstat sees this server coming from."""
    client = request.headers.get("x-forwarded-for", "").split(",")[0].strip()
    client = client or (request.client.host if request.client else "")
    try:
        if client and ip_address(client).is_global:
            address, via = client, "browser"
        else:
            address, via = ripe.my_ip(), "server"
        return JSONResponse({"ip": address, "via": via, **ripe.lookup(address)})
    except (ValueError, ripestat.RipeStatError) as exc:
        return JSONResponse({"error": str(exc)}, status_code=502)


@app.get("/api/lab")
def lab() -> JSONResponse:
    if not LAB_CONTROL:
        return JSONResponse(CONTROL_DISABLED, status_code=403)
    return JSONResponse(
        {"ok": True, "collector": labview.COLLECTOR, "routers": labview.snapshot(_stdout_of)}
    )


@app.post("/api/lab/trigger")
def lab_trigger(body: dict = Body(default={})) -> JSONResponse:
    scenario = body.get("scenario", "both")
    if scenario not in SCENARIOS:
        return JSONResponse({"ok": False, "error": "unknown scenario"}, status_code=400)
    return _lab_action("trigger", SCENARIOS[scenario])


@app.post("/api/lab/reset")
def lab_reset() -> JSONResponse:
    return _lab_action("reset", SCENARIOS["both"])


@app.post("/api/lab/trace")
def lab_trace(body: dict = Body(default={})) -> JSONResponse:
    """Where a packet from a lab router to an address of the lab prefix would go, asked
    of each router's kernel along the way. No packet is sent (see trace.py)."""
    if not LAB_CONTROL:
        return JSONResponse(CONTROL_DISABLED, status_code=403)
    router = next((r for r in labview.ROUTERS if r.role == body.get("from", "transit")), None)
    if router is None:
        return JSONResponse({"ok": False, "error": "unknown router"}, status_code=400)
    try:
        destination = ip_address(body.get("to", "203.0.113.1"))
    except ValueError:
        return JSONResponse({"ok": False, "error": "not an address"}, status_code=400)
    if destination not in LAB_PREFIX:
        return JSONResponse({"ok": False, "error": f"destination outside {LAB_PREFIX}"}, status_code=400)
    containers = {r.role: r.container for r in labview.ROUTERS}
    by_address = {r.address: r.role for r in labview.ROUTERS}

    def ask(role: str, address: str) -> dict:
        result = _exec_in(containers[role], [*trace.ROUTE_GET, address])
        stdout, stderr = (stream or b"" for stream in result.output)
        return trace.verdict(stdout.decode(errors="replace"), stderr.decode(errors="replace"))

    try:
        walked = trace.walk(router.role, str(destination), by_address, ask)
    except Exception as exc:  # docker errors are reported to the operator, not raised
        return JSONResponse({"ok": False, "error": str(exc)}, status_code=502)
    return JSONResponse({"ok": True, "from": router.role, "to": str(destination), **walked})


@app.post("/api/lab/replay")
def lab_replay() -> JSONResponse:
    """Reload the recorded incident from its files, replacing what was loaded."""
    if not LAB_CONTROL:
        return JSONResponse(CONTROL_DISABLED, status_code=403)
    try:
        result = _exec_in(COLLECTOR, REPLAY_COMMAND)
    except Exception as exc:  # docker errors are reported to the operator, not raised
        return JSONResponse({"ok": False, "error": str(exc)}, status_code=502)
    stdout, stderr = (stream or b"" for stream in result.output)
    if result.exit_code != 0:
        error = stderr.decode(errors="replace")[-600:]
        return JSONResponse({"ok": False, "error": error}, status_code=502)
    return JSONResponse({"ok": True, **json.loads(stdout)})


@app.post("/api/history/clear")
def history_clear() -> JSONResponse:
    """Empty the lab's history (the recorded incident stays) and have the routers
    announce their tables again."""
    if not LAB_CONTROL:
        return JSONResponse(CONTROL_DISABLED, status_code=403)
    with pool.connection() as conn:
        alerts = conn.execute("DELETE FROM alerts WHERE source = 'lab'").rowcount
        routes = conn.execute("DELETE FROM route_updates WHERE source = 'lab'").rowcount
    _resend_lab_routes()
    return JSONResponse({"ok": True, "alerts": alerts, "routes": routes})


def _monitored(conn) -> list[dict]:
    return conn.execute(
        """
        SELECT prefix::text AS prefix, expected_origins, description
        FROM monitored_prefixes ORDER BY prefix
        """
    ).fetchall()


def _incident_dir(name: str) -> Path:
    folder = INCIDENTS_DIR / name
    if not re.fullmatch(r"[a-z0-9-]+", name) or not (folder / "config.toml").is_file():
        raise HTTPException(status_code=404, detail="unknown incident")
    return folder


def _resend_lab_routes() -> None:
    """Ask every lab router to announce its table to the collector again."""
    for router in labview.ROUTERS:
        try:
            _exec_in(router.container, RESEND_COMMAND)
        except Exception:  # a stopped router has nothing to resend
            continue


@lru_cache(maxsize=1)
def _docker():
    import docker

    return docker.from_env()


def _exec_in(container: str, command: list[str]):
    """Run a command in a container, keeping its stdout and stderr apart."""
    return _docker().containers.get(container).exec_run(command, demux=True)


def _stdout_of(container: str, command: list[str]) -> str:
    """Run a command in a container and return what it printed on stdout."""
    return (_exec_in(container, command).output[0] or b"").decode(errors="replace")


def _vtysh_commands(action: str, prefixes: list[str]) -> list[str]:
    if action == "trigger":
        cmds = ["configure terminal"]
        cmds += [f"ip route {p} Null0" for p in prefixes]
        cmds += ["router bgp 65003", "address-family ipv4 unicast"]
        cmds += [f"network {p}" for p in prefixes]
        cmds.append("end")
        return cmds
    cmds = ["configure terminal", "router bgp 65003", "address-family ipv4 unicast"]
    cmds += [f"no network {p}" for p in prefixes]
    cmds += ["exit-address-family", "exit"]
    cmds += [f"no ip route {p} Null0" for p in prefixes]
    cmds.append("end")
    return cmds


def _lab_action(action: str, prefixes: list[str]) -> JSONResponse:
    if not LAB_CONTROL:
        return JSONResponse(CONTROL_DISABLED, status_code=403)
    command = ["vtysh"]
    for c in _vtysh_commands(action, prefixes):
        command += ["-c", c]
    try:
        result = _exec_in(ATTACKER, command)
    except Exception as exc:  # docker errors are reported to the operator, not raised
        return JSONResponse({"ok": False, "error": str(exc)}, status_code=502)
    # vtysh returns non-zero for benign cases (removing absent config, saving to the
    # read-only mounted frr.conf), so success is "the command ran": the live route and
    # alert tables are the ground truth. The exit code is still reported for inspection.
    output = b"".join(stream or b"" for stream in result.output).decode(errors="replace")
    return JSONResponse(
        {
            "ok": True,
            "action": action,
            "prefixes": prefixes,
            "exit_code": result.exit_code,
            "output": output[-600:],
        }
    )
