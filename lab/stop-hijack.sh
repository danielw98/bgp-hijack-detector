#!/usr/bin/env bash
# Reset the attacker (AS 65003) to its benign baseline: withdraw the announcements and
# drop the blackhole routes. Leaves the detector's recorded alerts intact for review.
set -euo pipefail

docker exec lab-attacker vtysh \
  -c "configure terminal" \
  -c "router bgp 65003" \
  -c "address-family ipv4 unicast" \
  -c "no network 203.0.113.0/24" \
  -c "no network 203.0.113.0/25" \
  -c "exit-address-family" \
  -c "exit" \
  -c "no ip route 203.0.113.0/24 Null0" \
  -c "no ip route 203.0.113.0/25 Null0" \
  -c "end"

echo "attacker reset to baseline (announcements withdrawn)"
