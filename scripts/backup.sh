#!/usr/bin/env bash
set -euo pipefail

BACKUP_DIR="${1:-./backups}"
mkdir -p "${BACKUP_DIR}"

TIMESTAMP=$(date -u +"%Y%m%d_%H%M%S")
FILENAME="school_admission_${TIMESTAMP}.dump"
FILEPATH="${BACKUP_DIR}/${FILENAME}"

echo "Starting PostgreSQL backup: ${FILEPATH}"

if docker compose ps db 2>/dev/null | grep -q "Up"; then
  docker compose exec -T db pg_dump -U postgres -Fc school_admission > "${FILEPATH}"
else
  # Host execution fallback
  DB_HOST="${DB_HOST:-localhost}"
  DB_PORT="${DB_PORT:-5432}"
  DB_USER="${DB_USER:-postgres}"
  DB_NAME="${DB_NAME:-school_admission}"
  PGPASSWORD="${DB_PASSWORD:-postgrespassword}" pg_dump -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" -Fc "${DB_NAME}" > "${FILEPATH}"
fi

# Calculate sha256
sha256sum "${FILEPATH}" > "${FILEPATH}.sha256"

echo "Backup completed successfully!"
echo "Archive:  ${FILEPATH}"
echo "Checksum: $(cat "${FILEPATH}.sha256")"
