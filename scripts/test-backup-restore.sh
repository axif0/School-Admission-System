#!/usr/bin/env bash
set -euo pipefail

echo "=== Running Backup & Restore Verification Test (Finding X4) ==="

TMP_BACKUP_DIR=$(mktemp -d)
trap 'rm -rf "${TMP_BACKUP_DIR}"' EXIT

# 1. Ensure test data exists in the database
API_URL="${API_URL:-http://localhost:8080}"
echo "Seeding test data via API at ${API_URL}..."

CLASS_RES=$(curl -s -f -X POST "${API_URL}/classes" \
  -H "Content-Type: application/json" \
  -d '{"name": "Backup Restore Test Class", "totalSeats": 20}') || {
  echo "WARNING: Could not connect to API at ${API_URL}, continuing with existing DB state."
}

# 2. Run backup.sh
echo "Running scripts/backup.sh..."
chmod +x scripts/backup.sh scripts/restore.sh
./scripts/backup.sh "${TMP_BACKUP_DIR}"

BACKUP_FILE=$(ls -t "${TMP_BACKUP_DIR}"/*.dump | head -n1)
echo "Generated backup file: ${BACKUP_FILE}"

# 3. Assert backup file, .sha256, and .counts exist
if [ ! -f "${BACKUP_FILE}" ]; then
  echo "FAIL: Backup file not found!"
  exit 1
fi
if [ ! -f "${BACKUP_FILE}.sha256" ]; then
  echo "FAIL: Checksum file not found!"
  exit 1
fi
if [ ! -f "${BACKUP_FILE}.counts" ]; then
  echo "FAIL: Manifest counts file not found!"
  exit 1
fi

echo "Verified: .dump, .sha256, and .counts all exist."
cat "${BACKUP_FILE}.counts"

# 4. Test corrupted backup rejection
echo "Testing corrupted backup rejection..."
CORRUPT_FILE="${TMP_BACKUP_DIR}/corrupted.dump"
cp "${BACKUP_FILE}" "${CORRUPT_FILE}"
sed "s|$(basename "${BACKUP_FILE}")|$(basename "${CORRUPT_FILE}")|g" "${BACKUP_FILE}.sha256" > "${CORRUPT_FILE}.sha256"
# Corrupt bytes in the dump file
echo "CORRUPTED_BYTES_HERE" >> "${CORRUPT_FILE}"

if ./scripts/restore.sh "${CORRUPT_FILE}" 2>/dev/null; then
  echo "FAIL: Corrupted backup was accepted by restore.sh!"
  exit 1
else
  echo "SUCCESS: Corrupted backup was rejected as expected."
fi

# 5. Test valid backup verification (scratch database)
echo "Testing valid backup verification in scratch database..."
./scripts/restore.sh "${BACKUP_FILE}"

echo "=== All Backup & Restore Verification Tests Passed! ==="
