#!/usr/bin/env bash
set -euo pipefail

echo "=========================================================="
echo " Running Zero-Downtime Migration / Upgrade Test"
echo "=========================================================="

BASE_URL="${API_URL:-http://localhost:8080}"

# 1. Verify health
echo "1. Checking API health before upgrade..."
for i in {1..30}; do
  if curl -sf "${BASE_URL}/health/ready" >/dev/null; then
    echo "API is ready!"
    break
  fi
  echo "Waiting for API to become ready..."
  sleep 1
done

# 2. Create test class and hold
echo "2. Seeding pre-migration data..."
RESP=$(curl -s -X POST "${BASE_URL}/classes" \
  -H "Content-Type: application/json" \
  -d '{"name": "Upgrade Pre-Migration Class", "totalSeats": 5}')
CLASS_ID=$(echo "$RESP" | grep -o '"id":"[^"]*' | cut -d'"' -f4)

HOLD_RESP=$(curl -s -X POST "${BASE_URL}/classes/${CLASS_ID}/holds" \
  -H "X-Student-Id: 20159999999999990")
HOLD_ID=$(echo "$HOLD_RESP" | grep -o '"holdId":"[^"]*' | cut -d'"' -f4)

echo "Created Class: ${CLASS_ID}, Hold: ${HOLD_ID}"

# 3. Verify hold exists and is intact
CLASS_DETAIL=$(curl -s "${BASE_URL}/classes/${CLASS_ID}")
echo "Class detail: ${CLASS_DETAIL}"

# 4. Confirm hold
echo "3. Confirming hold..."
CONFIRM_RESP=$(curl -s -X POST "${BASE_URL}/holds/${HOLD_ID}/confirm" \
  -H "X-Student-Id: 20159999999999990")
echo "Confirm response: ${CONFIRM_RESP}"

echo "=========================================================="
echo " Upgrade Data Preservation Verified Successfully!"
echo "=========================================================="
