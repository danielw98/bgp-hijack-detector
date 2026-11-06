#!/bin/sh
# The detector's command line, run inside the stack docker compose started (see bgpwatch.bat
# for the verbs). Files for replay are container paths, such as
# /data/incidents/youtube-2008/updates.20080224.1845.gz.
cd "$(dirname "$0")" || exit 1
if [ "$(docker inspect -f '{{.State.Running}}' app-bgpwatch 2>/dev/null)" != "true" ]; then
  echo "The detector is not running: docker compose up -d first." >&2
  exit 1
fi
exec docker compose exec app dotnet BGPWatch.dll "$@"
