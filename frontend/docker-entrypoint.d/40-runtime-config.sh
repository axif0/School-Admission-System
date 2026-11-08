#!/bin/sh
set -eu
API_URL="${API_URL:-http://localhost:8080}"
esc=$(printf '%s' "$API_URL" | sed 's/\\/\\\\/g; s/"/\\"/g')
printf 'window.__APP_CONFIG__ = { API_URL: "%s" };\n' "$esc" > /tmp/config.js
