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
  CLASSES_COUNT=$(docker compose exec -T db psql -U postgres -d school_admission -tAc "SELECT count(*) FROM classes;" 2>/dev/null || echo "0")
  HOLDS_COUNT=$(docker compose exec -T db psql -U postgres -d school_admission -tAc "SELECT count(*) FROM holds;" 2>/dev/null || echo "0")
  MIGRATIONS_COUNT=$(docker compose exec -T db psql -U postgres -d school_admission -tAc "SELECT count(*) FROM \"__EFMigrationsHistory\";" 2>/dev/null || echo "0")
else
  # Host execution fallback
  DB_HOST="${DB_HOST:-localhost}"
  DB_PORT="${DB_PORT:-5432}"
  DB_USER="${DB_USER:-postgres}"
  DB_NAME="${DB_NAME:-school_admission}"
  PGPASSWORD="${DB_PASSWORD:-postgrespassword}" pg_dump -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" -Fc "${DB_NAME}" > "${FILEPATH}"
  CLASSES_COUNT=$(PGPASSWORD="${DB_PASSWORD:-postgrespassword}" psql -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" -d "${DB_NAME}" -tAc "SELECT count(*) FROM classes;" 2>/dev/null || echo "0")
  HOLDS_COUNT=$(PGPASSWORD="${DB_PASSWORD:-postgrespassword}" psql -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" -d "${DB_NAME}" -tAc "SELECT count(*) FROM holds;" 2>/dev/null || echo "0")
  MIGRATIONS_COUNT=$(PGPASSWORD="${DB_PASSWORD:-postgrespassword}" psql -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" -d "${DB_NAME}" -tAc "SELECT count(*) FROM \"__EFMigrationsHistory\";" 2>/dev/null || echo "0")
fi

# Calculate sha256 (relative path so files can be verified anywhere)
(cd "${BACKUP_DIR}" && sha256sum "${FILENAME}" > "${FILENAME}.sha256")

# Write counts manifest
cat <<EOF > "${FILEPATH}.counts"
classes=${CLASSES_COUNT}
holds=${HOLDS_COUNT}
__EFMigrationsHistory=${MIGRATIONS_COUNT}
EOF

echo "Backup completed successfully!"
echo "Archive:  ${FILEPATH}"
echo "Checksum: $(cat "${FILEPATH}.sha256")"
echo "Manifest:"
cat "${FILEPATH}.counts"
