#!/usr/bin/env bash
# Reset the attacker (AS 65003) to its benign baseline: withdraw the announcements and
# drop the blackhole routes. Leaves the detector's recorded alerts intact for review.
# One vtysh call per prefix: vtysh stops at the first failing command, and removing a
# prefix that is not announced fails.
set -uo pipefail

for prefix in 203.0.113.0/24 203.0.113.0/25; do
  docker exec lab-attacker vtysh \
    -c "configure terminal" \
    -c "router bgp 65003" \
    -c "address-family ipv4 unicast" \
    -c "no network $prefix" \
    -c "exit-address-family" \
    -c "exit" \
    -c "no ip route $prefix Null0" \
    -c "end" > /dev/null 2>&1
done

echo "attacker reset to baseline (announcements withdrawn)"
