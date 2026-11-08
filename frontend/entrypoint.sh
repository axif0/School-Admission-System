#!/bin/sh
set -e

# Default API_URL if not provided
API_URL="${API_URL:-http://localhost:8080}"

# Generate runtime config.js
cat <<EOF > /usr/share/nginx/html/config.js
window.__APP_CONFIG__ = {
  API_URL: "${API_URL}"
};
EOF

echo "Generated runtime config with API_URL: ${API_URL}"

# Execute nginx
exec "$@"
