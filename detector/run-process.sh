#!/bin/sh
# Wrapper exabgp launches as its receive-process.
exec python3 -m bgpwatch.exabgp_process
