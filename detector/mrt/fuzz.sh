#!/bin/sh
# Builds the MRT parser and its mutation harness with ASan and UBSan and runs it on a
# seed file (plain or .gz). Any sanitizer report aborts with a non-zero exit code.
#
# Usage: sh detector/mrt/fuzz.sh <seed.mrt[.gz]> [iterations]
# Needs a gcc with the sanitizer runtimes; scripts/fuzz_mrt.py runs it in Docker.
set -e
seed="$1"
iterations="${2:-20000}"
work="$(mktemp -d)"
case "$seed" in
    *.gz) gzip -dc "$seed" > "$work/seed.mrt" ;;
    *) cp "$seed" "$work/seed.mrt" ;;
esac
cd "$(dirname "$0")"
cc -std=c11 -g -O1 -Wall -Wextra -Werror -fsanitize=address,undefined -fno-sanitize-recover=all \
    -o "$work/fuzz" fuzz.c mrt.c
"$work/fuzz" "$work/seed.mrt" "$iterations"
