# BGP route hijack detector

**English** | [Română](README.ro.md)

A course project for PTIRC (TCSI master's programme, National University of Science and Technology POLITEHNICA Bucharest). A defensive detector: it watches the BGP announcements for the monitored prefixes and raises an alert when another autonomous system announces them. The technique is the one used by ARTEMIS (FORTH-ICS) and BGPalerter (NTT).

It detects three situations:

- **false origin**: the exact prefix, announced by an AS that does not hold it;
- **sub-prefix**: a more specific part of the prefix, announced by a foreign AS;
- **RPKI-invalid**: an announcement that no route origin authorisation (ROA) allows.

Components: a lab of three FRR routers that report their routes over BMP (BGP Monitoring Protocol) to a monitoring station; the BGPWatch application in C# (the BMP station, the RIS Live client that watches real prefixes, the detector, the webhook notifications, the command line, the web pages); a Postgres database for the route and alert history; and a BGP parser in C that decodes both the lab's messages and the real data of the RIPE RIS archive.

**Live demo:** <https://student-dev.ro/bgp/> (the application), [`/guide`](https://student-dev.ro/bgp/guide) (the guide to read), [`/references`](https://student-dev.ro/bgp/references) (the readings). The site is in English by default; the **EN | RO** toggle in the top bar, or `?lang=ro` in the address, switches it to Romanian and remembers the choice.

![How it works, step 3: the Attacker claims 203.0.113.0/25 and the packet to 203.0.113.1 reaches it](media/app-story.png)

## Screenshots

| | |
|---|---|
| ![YouTube 2008: one row per RIS neighbour, red while it sent the traffic to AS17557](media/app-2008.png) | ![The history of the prefix 203.0.113.0/24 on every router](media/app-prefixes.png) |
| **YouTube 2008**, replayed from the real RIPE RIS rrc00 updates | **Prefixes**: where each router sends the prefix, over time |
| ![Looking up a real prefix through RIPEstat, with its RPKI state](media/app-lookup.png) | ![The BGP readings, in stages](media/references.png) |
| **Lookup**: a real prefix, with its origin and RPKI state from RIPEstat | **References**: 44 sources in 9 stages |
| ![The Monitor: watched prefixes on the real Internet, through RIS Live](media/app-monitor.png) | |
| **Monitor**: the watched prefixes, the origins the RIS neighbours hold now, the open incidents | |

`python scripts/screenshots.py` rewrites them, in English into `media/` and in Romanian into `media/ro/` (from the public instance by default; `--url http://localhost:8080/` for a local one, `--views monitor` for some of them only).

## Stack

| Layer | What it uses |
|---|---|
| Lab | FRRouting 10.4 in Docker Compose, internal network `10.10.0.0/24`, BMP Loc-RIB (RFC 7854, RFC 9069) |
| Parser | C11, a shared library (`libmrt.so`, `mrt.dll`): MRT (RFC 6396) and BGP UPDATE, IPv4 and IPv6, AS4_PATH; every read bounds-checked, every failure reported |
| Application | C# on .NET 10: ASP.NET Core, Razor Pages, a minimal JSON API, P/Invoke to the parser, the Docker Engine API over its socket |
| Languages | a C# translation service over one JSON file per language (`Localization/strings.en.json`, `strings.ro.json`), shared by the pages and the browser modules |
| Data | PostgreSQL 16 through Npgsql (binary COPY, no ORM); RIPE RIS Live over a WebSocket; the RIPEstat Data API, with a 10-minute cache |
| Page | ES modules without a framework, SVG, the Atkinson Hyperlegible Next and Mono fonts |
| Tests and checks | xUnit v3, Playwright for Python, ASan and UBSan for the parser |

## Isolation guarantees

- The lab network is internal (`internal: true` in `lab/docker-compose.yml`), with no uplink. No announcement leaves the machine.
- The routers use private AS numbers (65000, 65001, 65003) and the documentation prefix `203.0.113.0/24` (RFC 5737). The BMP station only listens: it has no BGP session and announces nothing.
- The lab hijack is a test: the "attacker" router announces the prefix only inside this closed network.
- The 2008 replay reads a recorded file. It contacts no network.
- Locally the page is published on a port of the machine; on the server it is served under the `/bgp` prefix. In both cases it drives the lab through the Docker socket, so anyone who sees the page can trigger and reset the test hijack, and nothing else.
- No real packet crosses the lab: Docker drops packets addressed outside the subnet of an internal network. "Ask the kernels" reads each router's routing decision instead of sending packets.
- The lookup view asks RIPEstat (`stat.ripe.net`) about a real prefix, and it only reads.
- The monitor opens one outgoing WebSocket to RIPE RIS Live (`ris-live.ripe.net`) for the watched prefixes, reads RIPEstat for their current routes and ROAs, and posts notifications only to the webhooks named in the watch file. Nothing announces anything: the detector is a listener everywhere.

## Install, start, stop

Running the application needs only Docker. The other dependencies (the .NET 10 SDK, gcc, Python with Playwright) serve the tests and the scripts in `scripts/`.

### Install

- **Windows 10/11:** double-click `install.bat`. It installs Docker Desktop, the .NET 10 SDK, MSYS2 with gcc and Python 3.12 through winget, then Playwright with Chromium and the NuGet packages, and skips what is already there. After the first install of Docker Desktop, restart the computer once (it needs WSL 2).
- **Debian or Ubuntu:** `sh scripts/install-linux.sh` from the repository root. It installs Docker Engine with the compose plugin, `build-essential`, the .NET 10 SDK in `~/.dotnet` and a `.venv` with Playwright and Chromium.

### Start and stop

- **Windows:** double-click `start.bat`. It starts Docker Desktop if it is stopped, then the lab and the detector, and opens the page. The page keeps the port of a stack already running, or takes the first free local port from 8080 to 8099. The first start builds the images and takes a few minutes. To stop, double-click `stop.bat`; the route history and the alerts stay in the Docker volume.
- **Linux or macOS**, from the repository root (`BGP_PORT` picks the local port, 8080 when unset; `.env.example` lists the variables a `.env` beside it may set):

```sh
# start: the lab, then the detector (BMP station, Postgres, the page)
docker compose -f lab/docker-compose.yml up -d
BGP_PORT=8080 docker compose up -d --build --remove-orphans

# stop
docker compose down
docker compose -f lab/docker-compose.yml down
```

### Useful Docker commands

```sh
docker compose ps                                   # state of the application and the database
docker compose logs -f app                          # the application's log
docker compose port app 8080                        # the local port the page is published on
docker compose -f lab/docker-compose.yml ps         # the three routers
docker exec lab-transit vtysh -c "show ip bgp"      # the transit router's BGP table
docker exec lab-transit vtysh -c "show bmp"         # its BMP session to the station
docker compose -f lab/docker-compose.yml up -d --force-recreate   # after a change in lab/frr/
docker compose down -v                              # stop and DELETE the Postgres history
```

The Postgres password in `docker-compose.yml` (`bgp`) is the default of the local demo; the database publishes no port.

## What each part does

The home page is the application: five views in the top bar (How it works, YouTube 2008, Prefixes, Lookup, Monitor) and the "Guide" and "References" buttons. The guide (`/guide`) is six short chapters to read from top to bottom, the first five with the story's map at the step they describe and a link to that step; every view of the application links to its chapter of the guide. In the guide, the dotted-underlined terms open a definition. The references (`/references`) are the readings for learning BGP, in stages, with the lineage of the specification from RFC 1105 to RFC 4271.

1. **How it works.** A route hijack in five steps, for a reader who has never heard of BGP: a still map of the lab on the left, one panel of text on the right, "Back" and "Next" (or the numbered dots) to move. The steps: a network announces its addresses, a packet goes where the routes say, the attacker claims half of the block and the packet reaches it, the detector raises an alert (with the real delay), and the provider's origin validation against the RPKI refuses the attacker's routes. Every fact comes from one cycle recorded on the lab's real routers (`static/story/cycle.json`, written by `scripts/record_story.py`), so the story works with the lab down. On the last step, "Run the attack on the real routers" announces the sub-prefix on the lab, reports when the detector raised its alert and where the packet went, and withdraws the attack. `#step-N` opens step N.
2. **YouTube 2008.** The whole incident on one screen: one row for each of the 11 neighbours of the rrc00 collector, with a lane showing where it sent the traffic for the hijacked block (red to AS17557, green to YouTube) and, on the right, the AS path at the chosen moment, with the autonomous system that hands the route to the origin framed. The lane axis is cut into phases at the five documented events: each phase is as wide as the square root of its duration, with the real duration written above it, and the much compressed phase (the 80 minutes of the hijack) carries the // break mark. Above sit "Play" (the cursor moves at one speed along the lane, and the label shows the current phase's pace), the time and how many neighbours send to each origin, then the RIPE NCC's documented event at the chosen moment, with buttons to the previous and the next. Dragging on the lanes or on the bar under them scrubs. The events, the first sighting at each neighbour and the raw data sit closed under the table. The real BGP updates (18:45-21:05 UTC, 136,344 updates) load once at start; the detector raises 31 sub-prefix alerts, the first at 18:47:57 UTC. "Reload the incident from its files", in the raw data, rewrites them without duplicates.
3. **The history of a prefix.** The watched prefix and the window (10 min, 1 hour, 24 h, all) are chosen at the top. The verdict says where each router sends the prefix now. Below, each router has one row for every prefix it saw (the watched prefix and its sub-prefixes), with the lane over the window (green to the expected origin, red to another AS, a thin line for no route) and its path now. The changes, the alerts and the raw data sit closed below.
4. **A real prefix.** A prefix or an address typed in, an example, or "My address". The answer starts with the RPKI verdict, then the covering prefix, the origin, the holder, the authorisations (ROAs) and what the RPKI state means, from RIPEstat. It only reads.
5. **Monitor.** The watched prefixes on the real Internet (the next section): the state of the RIS Live feed, one row per prefix with the expected origins, the origins the RIS neighbours hold now (how many neighbours each, red when unexpected), the last update and the status, then the open incidents and the last alerts. From the machine that runs the detector, or with the watch token, the row under the table adds a prefix (the expected origins and the holder come from RIPEstat once the prefix is typed), adds every prefix of an AS, or sends a test notification; "Expect AS... too" on a red origin and "Remove" edit a row.

## Watching real prefixes

The detector watches real prefixes the same way it watches the lab: the vantage points are the RIS neighbours of the RIPE NCC's route collectors, streamed by [RIS Live](https://ris-live.ripe.net/) (a free WebSocket, no key), and the three checks are the same. At start and whenever a prefix is added, its current routes, and those of every prefix announced inside it by an unexpected origin, are read from RIPEstat's looking glass and run through the detector, so a hijack already under way is caught at once; the ROAs that cover the watched prefixes and the prefixes announced inside them are read from RIPEstat too, at the seed and hourly, for the RPKI check.

The watch file `watch/bgpwatch.toml` holds everything (`watch/example.toml` is the commented example; the real file is never committed):

```toml
[[monitored]]
prefix = "193.0.0.0/21"
expected_origins = [3333]
description = "RIPE NCC"

[[webhook]]
name = "ops"
url = "https://example.org/hooks/bgp"
```

The alerts of one prefix and one offending origin form an incident. A webhook receives one JSON object when an incident opens, one when it closes (ten minutes without an alert, and no neighbour holds a route that would raise one: the route was withdrawn, or its origin is now expected), and one on a test; never one per update. The lab's own hijack, run from the story, goes through the same desk under the source `lab`, so the webhook can be tried without waiting for a real hijack:

```json
{"kind": "opened", "source": "ris", "at": "2026-11-06T18:40:01Z",
 "text": "more-specific 193.0.1.0/24 of monitored 193.0.0.0/21 announced by AS64496, expected AS3333; seen by 12 vantage points",
 "incident": {"prefix": "193.0.0.0/21", "origin": 64496, "expected_origins": [3333], "first_seen": "...", "last_seen": "...",
              "alerts": 12, "peers": ["rrc21 5.57.80.113"], "prefixes": ["193.0.1.0/24"], "alert_types": ["subprefix_hijack"], "detail": "..."}}
```

The page edits the prefixes of the file; the webhooks are edited in the file only. On the public instance the edits need the token set in `WATCH_TOKEN`; without a token, only a client on a private network (the machine that runs the stack) may edit. The page adds prefixes of at least /8 (IPv4) or /16 (IPv6), at most 50 from one AS and 100 in all; the command line has no such limit. The running detector applies the file within five seconds of any change.

The command line, `bgp-watch`, runs the same code without the page. `bgp-watch.bat` (Windows) or `./bgp-watch.sh` runs it inside the container while the stack is up, and the local build otherwise (`dotnet build BGPWatch.slnx` after `sh detector/mrt/build.sh`). `bgp-watch help` prints the manual in the shape of a man page, `bgp-watch help <command>` the section of one command:

| Command | What it does |
|---|---|
| `help [COMMAND]` | the manual, or the section of one command |
| `prefixes list` / `prefixes add PREFIX ASN... [--description TEXT]` / `prefixes remove PREFIX` | reads and edits the watch file |
| `check PREFIX` | what RIPEstat sees for the prefix today, against the file; exit code 2 on an unexpected origin |
| `watch [--json]` | a headless monitor: the alerts and the incidents on stdout (times in UTC), the webhooks on, no database |
| `replay FILE... [--json]` | the detector over MRT files (the 2008 recording, for one); exit code 2 when it raised anything |
| `notify test` | one test notification per webhook |
| `serve` | the web application itself, what the container runs |
| `dump FILE`, `measure CONFIG FILE` | the author's verbs: every route of an MRT file, and a measurement of the detector on it |

`--config FILE` names another watch file (default: `WATCH_CONFIG`, then `watch/bgpwatch.toml`); `--json` prints one JSON object per line in the words of the webhook payload.

Limits worth knowing: RIS Live streams updates, not tables, so the seed from RIPEstat is what the monitor knows at start; a restart re-announces an incident still under way. A legitimate prefix that starts being announced inside a watched one can raise RPKI-invalid alerts for up to nine hours: RIPEstat lists it within eight, and its ROAs are read at the next hourly refresh.

## Further reading

- `src/BGPWatch.Web/Reading/readings.en.md` and `readings.ro.md`: the BGP reading list, in English and in Romanian, in stages, with the lineage of the specification; the `/references` page is generated from them.
- `detector/incidents/youtube-2008/config.toml`: the source of the replayed data, the monitored prefix and the documented timeline of the incident, in both languages.

## Layout

- `lab/`: the FRR configurations and the scripts that start and stop the test hijack.
- `src/BGPWatch.Core/`: the core, with no dependencies: the domain model, detection, grouping alerts into incidents, the history of a prefix, the use cases.
- `src/BGPWatch.Web/`: the adapters and the host: the BMP station, the binding to the C parser, Postgres, Docker, RIPEstat, the API, the translation service (`Localization/`) and the pages (Razor Pages, `wwwroot/static/`).
- `tests/BGPWatch.Tests/`: the unit tests.
- `detector/mrt/`: the BGP and MRT parser in C, with its sanitizer fuzz harness.
- `detector/incidents/`, `detector/config.toml`, `detector/schema.sql`: the 2008 incident's data, the lab configuration and the database schema.
- `watch/`: the watch file of the real-Internet monitor (`example.toml` commented; `bgpwatch.toml` is yours, never committed); `bgp-watch.bat` and `bgp-watch.sh` run the command line.
- `scripts/`: the installers, the page check, the lab measurements, the parser fuzzing, the screenshots in `media/`.
- `docker-compose.yml` is the production shape; `docker-compose.override.yml` (loaded by default) adds the local port and the files mounted from the working tree; `docker-compose.server.yml` and `deploy/nginx-bgp.conf` are the server layer.

## Tests and checks

- Unit tests: `sh detector/mrt/build.sh`, then `dotnet test --solution BGPWatch.slnx`; the browser modules: `node --test "tests/js/*.test.mjs"`.
- Page check, with the stack running: `python scripts/check_page.py` (both languages at 390, 1366 and 1920 px: overflow, script errors, map text size and collisions, token contrast, emoji).
- Parser fuzzing under ASan and UBSan: `python scripts/fuzz_mrt.py 20000`.

## License

MIT, see [LICENSE](LICENSE). The vendored Atkinson Hyperlegible fonts are under the SIL Open Font License; the 2008 RIPE RIS files are RIPE NCC data.
