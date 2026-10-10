#!/bin/sh
# Builds the MRT parser as the shared library the app loads: build/libmrt.so, or
# build/mrt.dll under MSYS2/MinGW for the tests on Windows.
set -e
cd "$(dirname "$0")"
mkdir -p build
case "$(uname -s)" in
    MINGW* | MSYS*) out=build/mrt.dll ;;
    *) out=build/libmrt.so ;;
esac
cc -std=c11 -O2 -Wall -Wextra -Werror -shared -fPIC -o "$out" mrt.c
