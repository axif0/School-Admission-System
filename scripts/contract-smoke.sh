#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${API_URL:-http://localhost:8080}"
echo "=========================================================="
echo " Running Section 6 Strict API Contract Smoke Tests"
echo " Target: ${BASE_URL}"
echo "=========================================================="

command -v curl >/dev/null 2>&1 || { echo "curl is required"; exit 1; }
command -v jq >/dev/null 2>&1 || { echo "jq is required"; exit 1; }

PASS_COUNT=0
FAIL_COUNT=0

assert_eq() {
  local expected="$1"
  local actual="$2"
  local msg="$3"
  if [ "$expected" = "$actual" ]; then
    echo "  [PASS] $msg"
    PASS_COUNT=$((PASS_COUNT + 1))
  else
    echo "  [FAIL] $msg (expected: '$expected', got: '$actual')"
    FAIL_COUNT=$((FAIL_COUNT + 1))
    exit 1
  fi
}

# 1. POST /classes
echo "1. POST /classes (Create Class)"
RESP=$(curl -s -w "\n%{http_code}" -X POST "${BASE_URL}/classes" \
  -H "Content-Type: application/json" \
  -d '{"name": "Contract Smoke Class", "totalSeats": 2}')
CODE=$(echo "$RESP" | tail -n1)
BODY=$(echo "$RESP" | sed '$d')
assert_eq "201" "$CODE" "Status is 201"
CLASS_ID=$(echo "$BODY" | jq -r '.id')
assert_eq "true" "$(echo "$BODY" | jq -r '.id | startswith("c_")')" "Class ID has prefix 'c_'"
assert_eq "Contract Smoke Class" "$(echo "$BODY" | jq -r '.name')" "Class name matches"
assert_eq "2" "$(echo "$BODY" | jq -r '.totalSeats')" "Total seats is 2"

# 2. GET /classes
echo "2. GET /classes (List Classes)"
RESP=$(curl -s -w "\n%{http_code}" "${BASE_URL}/classes")
CODE=$(echo "$RESP" | tail -n1)
BODY=$(echo "$RESP" | sed '$d')
assert_eq "200" "$CODE" "Status is 200"
MATCH=$(echo "$BODY" | jq -r ".[] | select(.id == \"${CLASS_ID}\")")
assert_eq "${CLASS_ID}" "$(echo "$MATCH" | jq -r '.id')" "Created class present in list"

# 3. GET /classes/{classId}
echo "3. GET /classes/{classId} (Get Class Details)"
RESP=$(curl -s -w "\n%{http_code}" "${BASE_URL}/classes/${CLASS_ID}")
CODE=$(echo "$RESP" | tail -n1)
BODY=$(echo "$RESP" | sed '$d')
assert_eq "200" "$CODE" "Status is 200"
assert_eq "${CLASS_ID}" "$(echo "$BODY" | jq -r '.id')" "Class ID matches"
assert_eq "2" "$(echo "$BODY" | jq -r '.availableSeats')" "Available seats initially 2"

# 4. POST /classes/{classId}/holds (Hold 1)
echo "4. POST /classes/{classId}/holds (Create Hold)"
STUDENT1="20158888888888881"
RESP=$(curl -s -w "\n%{http_code}" -X POST "${BASE_URL}/classes/${CLASS_ID}/holds" \
  -H "X-Student-Id: ${STUDENT1}")
CODE=$(echo "$RESP" | tail -n1)
BODY=$(echo "$RESP" | sed '$d')
assert_eq "201" "$CODE" "Status is 201"
HOLD_ID=$(echo "$BODY" | jq -r '.holdId')
assert_eq "true" "$(echo "$BODY" | jq -r '.holdId | startswith("h_")')" "Hold ID has prefix 'h_'"
assert_eq "HELD" "$(echo "$BODY" | jq -r '.status')" "Status is HELD"
assert_eq "${STUDENT1}" "$(echo "$BODY" | jq -r '.studentId')" "Student ID matches"
assert_eq "true" "$(echo "$BODY" | jq -r '.expiresAt | endswith("Z")')" "ExpiresAt ends with Z"

# 5. GET /holds/{holdId}
echo "5. GET /holds/{holdId} (Get Hold)"
RESP=$(curl -s -w "\n%{http_code}" "${BASE_URL}/holds/${HOLD_ID}")
CODE=$(echo "$RESP" | tail -n1)
BODY=$(echo "$RESP" | sed '$d')
assert_eq "200" "$CODE" "Status is 200"
assert_eq "${HOLD_ID}" "$(echo "$BODY" | jq -r '.holdId')" "Hold ID matches"
assert_eq "HELD" "$(echo "$BODY" | jq -r '.status')" "Status is HELD"

# 6. POST /holds/{holdId}/confirm
echo "6. POST /holds/{holdId}/confirm (Confirm Hold)"
RESP=$(curl -s -w "\n%{http_code}" -X POST "${BASE_URL}/holds/${HOLD_ID}/confirm" \
  -H "X-Student-Id: ${STUDENT1}")
CODE=$(echo "$RESP" | tail -n1)
BODY=$(echo "$RESP" | sed '$d')
assert_eq "200" "$CODE" "Status is 200"
assert_eq "CONFIRMED" "$(echo "$BODY" | jq -r '.status')" "Status is CONFIRMED"

# 7. DELETE /holds/{holdId} (Hold 2)
echo "7. DELETE /holds/{holdId} (Cancel Active Hold)"
STUDENT2="20158888888888882"
RESP=$(curl -s -w "\n%{http_code}" -X POST "${BASE_URL}/classes/${CLASS_ID}/holds" \
  -H "X-Student-Id: ${STUDENT2}")
HOLD_ID2=$(echo "$RESP" | sed '$d' | jq -r '.holdId')

RESP=$(curl -s -w "\n%{http_code}" -X DELETE "${BASE_URL}/holds/${HOLD_ID2}" \
  -H "X-Student-Id: ${STUDENT2}")
CODE=$(echo "$RESP" | tail -n1)
assert_eq "204" "$CODE" "Delete status is 204"

# 8. GET /me/holds
echo "8. GET /me/holds (My Holds)"
RESP=$(curl -s -w "\n%{http_code}" "${BASE_URL}/me/holds" \
  -H "X-Student-Id: ${STUDENT1}")
CODE=$(echo "$RESP" | tail -n1)
BODY=$(echo "$RESP" | sed '$d')
assert_eq "200" "$CODE" "Status is 200"
assert_eq "CONFIRMED" "$(echo "$BODY" | jq -r '.[0].status')" "My hold status is CONFIRMED"

# 9. Overbooking Conflict -> 409
echo "9. Overbooking test -> 409 Conflict"
STUDENT3="20158888888888883"
STUDENT4="20158888888888884"
# Fill remaining seat
curl -s -X POST "${BASE_URL}/classes/${CLASS_ID}/holds" -H "X-Student-Id: ${STUDENT3}" >/dev/null
# 3rd student attempts hold on 2-seat class
RESP=$(curl -s -w "\n%{http_code}" -X POST "${BASE_URL}/classes/${CLASS_ID}/holds" \
  -H "X-Student-Id: ${STUDENT4}")
CODE=$(echo "$RESP" | tail -n1)
BODY=$(echo "$RESP" | sed '$d')
assert_eq "409" "$CODE" "Overbooked hold returns 409"
assert_eq "NO_SEATS_AVAILABLE" "$(echo "$BODY" | jq -r '.error.code')" "Error code is NO_SEATS_AVAILABLE"
assert_eq "true" "$(echo "$BODY" | jq -r '.error.traceId | length > 0')" "Error contains traceId"

echo "=========================================================="
echo " ALL CONTRACT SMOKE TESTS PASSED (${PASS_COUNT} assertions)"
echo "=========================================================="
