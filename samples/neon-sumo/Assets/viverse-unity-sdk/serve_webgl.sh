#!/usr/bin/env bash
#
# serve_webgl.sh — Local server for VIVERSE Unity SDK WebGL builds
#
# Required because:
#   1. OAuth callback must come back to http://localhost:40078 (the only redirect URI
#      registered with the SDK's shared dev OAuth client).
#   2. CORS workaround: viveport.com APIs do not allow CORS from arbitrary localhosts,
#      so this server proxies them transparently.
#
# Features:
#   - Serves any Unity WebGL build folder with correct Content-Encoding for .gz files.
#   - Reverse proxy for VIVERSE backends (transparent to your app code):
#       /api/vrleaderboard/*  → https://www.viveport.com/api/vrleaderboard/*
#       /api/ironhide/*       → https://www.viveport.com/api/ironhide/*
#       /api/optimusprime/*   → https://www.viveport.com/api/optimusprime/*
#       /api/avatar/*         → https://sdk-api.viverse.com/*
#       /api/avatar-files/*   → https://avatar.viverse.com/*
#   - Idempotent port cleanup (kills any previous instance on the port).
#
# Usage:
#   ./serve_webgl.sh                       # serves ./Build (Unity's default WebGL output)
#   ./serve_webgl.sh ./MyBuild             # serves ./MyBuild
#   ./serve_webgl.sh --port 40078 ./Build  # custom port (OAuth requires 40078)
#   ./serve_webgl.sh -h                    # show this help
#
# Requirements:
#   - bash, python3 (≥ 3.7), lsof
#

set -euo pipefail

PORT=40078
BUILD_DIR=""

print_help() {
  sed -n '2,28p' "$0" | sed 's/^# \{0,1\}//'
  exit 0
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) print_help ;;
    -p|--port)
      if [[ $# -lt 2 ]]; then echo "Error: --port requires a value." >&2; exit 1; fi
      PORT="$2"; shift 2 ;;
    --port=*) PORT="${1#*=}"; shift ;;
    -*) echo "Error: unknown option '$1' (use -h for help)" >&2; exit 1 ;;
    *)
      if [[ -n "$BUILD_DIR" ]]; then
        echo "Error: multiple build paths given ('$BUILD_DIR' and '$1')." >&2; exit 1
      fi
      BUILD_DIR="$1"; shift ;;
  esac
done

BUILD_DIR="${BUILD_DIR:-./Build}"

if [[ ! -d "$BUILD_DIR" ]]; then
  echo "Error: build directory '$BUILD_DIR' not found." >&2
  echo "Build your WebGL project first, then pass the build folder as the first argument." >&2
  echo "Run with -h for usage details." >&2
  exit 1
fi

BUILD_DIR_ABS="$(cd "$BUILD_DIR" && pwd)"

if [[ ! -f "$BUILD_DIR_ABS/index.html" ]]; then
  echo "Error: '$BUILD_DIR_ABS/index.html' not found." >&2
  echo "The given directory does not look like a Unity WebGL build output." >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "Error: python3 is required but not found in PATH." >&2
  exit 1
fi

if command -v lsof >/dev/null 2>&1; then
  if PIDS=$(lsof -ti:"$PORT" 2>/dev/null) && [[ -n "$PIDS" ]]; then
    echo "Port $PORT already in use (PIDs: $PIDS). Killing previous process(es)..."
    kill -9 $PIDS 2>/dev/null || true
    sleep 1
  fi
fi

echo "─────────────────────────────────────────────────────────"
echo " VIVERSE Unity SDK — WebGL local server"
echo "─────────────────────────────────────────────────────────"
echo " Serving : $BUILD_DIR_ABS"
echo " URL     : http://localhost:$PORT"
echo " Proxies : /api/vrleaderboard, /api/ironhide, /api/optimusprime → viveport.com"
echo "           /api/avatar         → sdk-api.viverse.com"
echo "           /api/avatar-files   → avatar.viverse.com"
echo " Stop    : Ctrl+C"
echo "─────────────────────────────────────────────────────────"
echo ""

export VIVERSE_SERVE_PORT="$PORT"
export VIVERSE_SERVE_DIR="$BUILD_DIR_ABS"

exec python3 - <<'PYEOF'
import http.server
import os
import ssl
import sys
import urllib.error
import urllib.request

PORT = int(os.environ["VIVERSE_SERVE_PORT"])
SERVE_DIR = os.environ["VIVERSE_SERVE_DIR"]

PROXY_RULES = [
    ("/api/vrleaderboard/", "https://www.viveport.com", "keep"),
    ("/api/ironhide/",      "https://www.viveport.com", "keep"),
    ("/api/optimusprime/",  "https://www.viveport.com", "keep"),
    ("/api/avatar-files/",  "https://avatar.viverse.com", "strip"),
    ("/api/avatar/",        "https://sdk-api.viverse.com", "strip"),
]

FORWARD_HEADERS = (
    "Content-Type", "AccessToken", "Authkey", "AuthKey", "Token",
    "x-htc-public-key", "x-htc-public-key-format", "x-htc-op-token", "accesstoken",
)

GZIP_MIME = {
    ".js.gz":     "application/javascript",
    ".wasm.gz":   "application/wasm",
    ".data.gz":   "application/octet-stream",
    ".symbols.json.gz": "application/json",
}

os.chdir(SERVE_DIR)


class Handler(http.server.SimpleHTTPRequestHandler):
    server_version = "ViverseSDKServe/1.0"

    def _match_rule(self):
        for prefix, target, mode in PROXY_RULES:
            if self.path.startswith(prefix):
                if mode == "strip":
                    suffix = self.path[len(prefix):]
                    return f"{target}/{suffix}"
                return f"{target}{self.path}"
        return None

    def guess_type(self, path):
        for suffix, mime in GZIP_MIME.items():
            if path.endswith(suffix):
                return mime
        return super().guess_type(path)

    def end_headers(self):
        if self.path.endswith(".gz"):
            self.send_header("Content-Encoding", "gzip")
        super().end_headers()

    def _proxy(self, method, body=None):
        target_url = self._match_rule()
        headers = {h: self.headers[h] for h in FORWARD_HEADERS if self.headers.get(h)}

        is_avatar_files = self.path.startswith("/api/avatar-files/")
        timeout = 120 if is_avatar_files else 30
        ctx = None
        if is_avatar_files:
            ctx = ssl.create_default_context()
            ctx.check_hostname = False
            ctx.verify_mode = ssl.CERT_NONE

        try:
            req = urllib.request.Request(target_url, data=body, headers=headers, method=method)
            with urllib.request.urlopen(req, timeout=timeout, context=ctx) as resp:
                resp_body = resp.read()
                self.send_response(resp.status)
                for h in ("Content-Type", "Content-Length"):
                    v = resp.getheader(h)
                    if v:
                        self.send_header(h, v)
                self.send_header("Access-Control-Allow-Origin", "*")
                self.end_headers()
                self.wfile.write(resp_body)
        except urllib.error.HTTPError as e:
            resp_body = e.read()
            print(f"[Proxy] HTTP {e.code} from {target_url}: {resp_body[:200]!r}", file=sys.stderr)
            self.send_response(e.code)
            self.send_header("Content-Type", e.headers.get("Content-Type", "application/json"))
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
            self.wfile.write(resp_body)
        except Exception as e:
            print(f"[Proxy] Error fetching {target_url}: {e}", file=sys.stderr)
            self.send_response(502)
            self.send_header("Content-Type", "text/plain")
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
            self.wfile.write(str(e).encode())

    def do_GET(self):
        if self._match_rule():
            self._proxy("GET")
        else:
            super().do_GET()

    def do_POST(self):
        if self._match_rule():
            length = int(self.headers.get("Content-Length", 0))
            body = self.rfile.read(length) if length > 0 else None
            self._proxy("POST", body)
        else:
            self.send_response(405)
            self.end_headers()

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header(
            "Access-Control-Allow-Headers",
            "Content-Type, AccessToken, Authkey, AuthKey, Token, "
            "x-htc-public-key, x-htc-public-key-format, x-htc-op-token, accesstoken",
        )
        self.send_header("Access-Control-Max-Age", "86400")
        self.end_headers()


server = http.server.HTTPServer(("localhost", PORT), Handler)
print(f"Ready. Open http://localhost:{PORT}", flush=True)
try:
    server.serve_forever()
except KeyboardInterrupt:
    print("\nStopped.")
    sys.exit(0)
PYEOF
