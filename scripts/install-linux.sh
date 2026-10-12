#!/bin/sh
# Installs every dependency of the project on Debian or Ubuntu: Docker Engine with the
# compose plugin (runs the lab and the app), gcc (builds the C parser), the .NET 10 SDK
# in ~/.dotnet (builds and tests the app), and a .venv with Playwright and Chromium
# (the author scripts and the page gate). Skips what is already installed.
# Usage, from the repo root: sh scripts/install-linux.sh
set -e
cd "$(dirname "$0")/.."

SUDO=""
[ "$(id -u)" -ne 0 ] && SUDO="sudo"

echo "[..] apt packages"
$SUDO apt-get update -qq
$SUDO apt-get install -y -qq ca-certificates curl build-essential python3 python3-venv >/dev/null

if command -v docker >/dev/null 2>&1; then
    echo "[ok] Docker"
else
    echo "[..] Docker Engine and compose plugin"
    curl -fsSL https://get.docker.com | $SUDO sh
fi
if [ -n "$SUDO" ] && ! id -nG | grep -qw docker; then
    $SUDO usermod -aG docker "$(id -un)"
    echo "[!!] added $(id -un) to the docker group: log out and back in once"
fi

if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks | grep -q '^10\.'; then
    echo "[ok] .NET 10 SDK"
else
    echo "[..] .NET 10 SDK into ~/.dotnet"
    curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
    grep -q 'DOTNET_ROOT' "$HOME/.profile" 2>/dev/null || cat >>"$HOME/.profile" <<'EOF'
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
EOF
    export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"
fi

echo "[..] .venv with Playwright and Chromium"
python3 -m venv .venv
.venv/bin/pip install --quiet --upgrade playwright
$SUDO .venv/bin/python -m playwright install-deps chromium >/dev/null
.venv/bin/python -m playwright install chromium

echo "[..] NuGet packages"
dotnet restore BGPWatch.slnx

echo
echo "Done. Start: docker compose -f lab/docker-compose.yml up -d && docker compose up -d --build"
