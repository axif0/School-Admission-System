#!/usr/bin/env bash
# ==============================================================================
# Bonus Multi-Process Concurrency Verification ("Hammer")
# Fires concurrent hold requests simultaneously at TWO backend processes
# sharing ONE PostgreSQL database:
#   Backend 1: http://localhost:8080
#   Backend 2: http://localhost:8081
#
# Verifies that held + confirmed <= totalSeats holds true across both processes.
# ==============================================================================

set -euo pipefail

BACKEND1="http://localhost:8080"
BACKEND2="http://localhost:8081"
TOTAL_SEATS=15
BURST_COUNT=100

echo "=== School Admission Multi-Process Concurrency Hammer ==="

# Check health of both backends
echo "Checking backend 1 at $BACKEND1/health..."
curl -sf "$BACKEND1/health" > /dev/null || { echo "Backend 1 ($BACKEND1) is not healthy!"; exit 1; }

echo "Checking backend 2 at $BACKEND2/health..."
curl -sf "$BACKEND2/health" > /dev/null || { echo "Backend 2 ($BACKEND2) is not healthy!"; exit 1; }

# 1. Create a class with 15 seats on Backend 1
echo "Creating test class with $TOTAL_SEATS seats via Backend 1..."
CREATE_RES=$(curl -sf -X POST "$BACKEND1/classes" \
  -H "Content-Type: application/json" \
  -d "{\"name\": \"Multi-Process Concurrency Test\", \"totalSeats\": $TOTAL_SEATS}")

CLASS_ID=$(echo "$CREATE_RES" | grep -o '"id":"[^"]*' | cut -d'"' -f4)
echo "Created class: $CLASS_ID with $TOTAL_SEATS seats."

# 2. Fire simultaneous parallel requests distributed between Backend 1 and Backend 2
echo "Firing $BURST_COUNT simultaneous hold requests across both backends..."
PIDS=()
RESULTS_DIR=$(mktemp -d)

for i in $(seq 1 $BURST_COUNT); do
  STUDENT_ID=$(printf "2026%013d" "$i")
  # Alternate between Backend 1 and Backend 2
  if (( i % 2 == 1 )); then
    TARGET="$BACKEND1/classes/$CLASS_ID/holds"
  else
    TARGET="$BACKEND2/classes/$CLASS_ID/holds"
  fi

  (
    CODE=$(curl -s -o "$RESULTS_DIR/res_$i.json" -w "%{http_code}" -X POST "$TARGET" \
      -H "X-Student-Id: $STUDENT_ID")
    echo "$CODE" > "$RESULTS_DIR/code_$i.txt"
  ) &
  PIDS+=($!)
done

# Wait for all background requests
for pid in "${PIDS[@]}"; do
  wait "$pid"
done

# 3. Analyze results
SUCCESS_COUNT=0
CONFLICT_COUNT=0
OTHER_COUNT=0

for i in $(seq 1 $BURST_COUNT); do
  CODE=$(cat "$RESULTS_DIR/code_$i.txt")
  if [ "$CODE" = "201" ]; then
    SUCCESS_COUNT=$((SUCCESS_COUNT + 1))
  elif [ "$CODE" = "409" ]; then
    CONFLICT_COUNT=$((CONFLICT_COUNT + 1))
  else
    OTHER_COUNT=$((OTHER_COUNT + 1))
    echo "Unexpected status code $CODE on request $i"
  fi
done

rm -rf "$RESULTS_DIR"

echo "Results:"
echo "  Success (201 Created): $SUCCESS_COUNT"
echo "  Conflict (409 No Seats): $CONFLICT_COUNT"
echo "  Other: $OTHER_COUNT"

# Verify seat counts from Backend 2
CLASS_DETAIL=$(curl -sf "$BACKEND2/classes/$CLASS_ID")
echo "Class status from Backend 2: $CLASS_DETAIL"

if [ "$SUCCESS_COUNT" -eq "$TOTAL_SEATS" ] && [ "$OTHER_COUNT" -eq 0 ]; then
  echo "✅ SUCCESS: Invariant preserved! Exactly $TOTAL_SEATS holds succeeded across both backends."
  exit 0
else
  echo "❌ FAILURE: Invariant violated! Expected $TOTAL_SEATS successes, got $SUCCESS_COUNT."
  exit 1
fi
