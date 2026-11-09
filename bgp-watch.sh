#!/bin/sh
# bgp-watch: the detector's command line. Runs inside the stack docker compose started,
# or, without the stack, the local build. "bgp-watch help" prints the manual. Files for
# replay inside the stack are container paths, such as
# /data/incidents/youtube-2008/updates.20080224.1845.gz.
cd "$(dirname "$0")" || exit 1
if [ "$(docker inspect -f '{{.State.Running}}' app-bgpwatch 2>/dev/null)" = "true" ]; then
  exec docker compose exec app dotnet bgp-watch.dll "${@:-help}"
fi
for local in src/BGPWatch.Web/bin/Debug/net10.0/bgp-watch src/BGPWatch.Web/bin/Release/net10.0/bgp-watch; do
  if [ -x "$local" ]; then
    exec "$local" "${@:-help}"
  fi
done
echo "The detector is not running and there is no local build: docker compose up -d, or dotnet build BGPWatch.slnx." >&2
exit 1