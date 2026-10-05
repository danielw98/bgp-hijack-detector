#!/bin/sh
# Builds the MRT parser as the shared library bgpwatch.mrt loads: build/libmrt.so.
set -e
cd "$(dirname "$0")"
mkdir -p build
cc -std=c11 -O2 -Wall -Wextra -Werror -shared -fPIC -o build/libmrt.so mrt.c
