#!/usr/bin/env bash
# RA-04: start local SS14 server, wait for port, then start client.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CONFIG="${CONFIG:-$ROOT/Cognition/config/server_local.toml}"
PORT="${PORT:-1212}"
CONFIGURATION="${CONFIGURATION:-Release}"
NO_CLIENT=0
RELEASE_FLAG=()

usage() {
  cat <<EOF
Usage: $(basename "$0") [--release] [--no-client] [--config PATH] [--port N]

  --release     Use Release configuration (default)
  --no-client   Start headless server only (skip client)
  --config PATH Server --config-file path (default: Cognition/config/server_local.toml)
  --port N      Wait for this TCP port (default: 1212)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --release) CONFIGURATION=Release; shift ;;
    --no-client) NO_CLIENT=1; shift ;;
    --config) CONFIG="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown arg: $1"; usage; exit 1 ;;
  esac
done

if [[ ! -f "$CONFIG" ]]; then
  echo "Config not found: $CONFIG" >&2
  exit 1
fi

SERVER_PID=""
cleanup() {
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

mkdir -p "$ROOT/Cognition/data/server"

SERVER_BIN="$ROOT/bin/Content.Server/Content.Server"
CLIENT_BIN="$ROOT/bin/Content.Client/Content.Client"

echo "Starting server ($CONFIGURATION) with $CONFIG ..."
if [[ -x "$SERVER_BIN" ]]; then
  (cd "$ROOT/bin/Content.Server" && ./Content.Server \
    --config-file "$CONFIG" \
    --data-dir "$ROOT/Cognition/data/server") &
else
  dotnet run --project Content.Server -c "$CONFIGURATION" -- \
    --config-file "$CONFIG" \
    --data-dir "$ROOT/Cognition/data/server" &
fi
SERVER_PID=$!

# First boot (EF migrations + map load) can take 60–120s under load.
echo "Waiting for port $PORT (up to 180s) ..."
for i in $(seq 1 180); do
  if (echo >/dev/tcp/127.0.0.1/"$PORT") >/dev/null 2>&1; then
    echo "Server port $PORT is open after ${i}s."
    break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    echo "Server process exited before port opened." >&2
    exit 1
  fi
  sleep 1
  if [[ "$i" -eq 180 ]]; then
    echo "Timeout waiting for port $PORT." >&2
    exit 1
  fi
done

if [[ "$NO_CLIENT" -eq 1 ]]; then
  echo "Headless mode (--no-client). Server PID=$SERVER_PID. Ctrl+C to stop."
  wait "$SERVER_PID"
  exit 0
fi

echo "Starting client (--connect) ..."
if [[ -x "$CLIENT_BIN" ]]; then
  (cd "$ROOT/bin/Content.Client" && ./Content.Client \
    --connect --connect-address "localhost:$PORT")
else
  dotnet run --project Content.Client -c "$CONFIGURATION" -- \
    --connect --connect-address "localhost:$PORT"
fi

echo "Client exited; stopping server."
