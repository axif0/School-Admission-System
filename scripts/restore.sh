#!/usr/bin/env bash
set -euo pipefail

ARCHIVE="${1:-}"
FORCE="${2:-}"

if [ -z "${ARCHIVE}" ] || [ ! -f "${ARCHIVE}" ]; then
  echo "Usage: $0 <path_to_dump> [--force]"
  exit 1
fi

echo "Verifying archive: ${ARCHIVE}"

if [ -f "${ARCHIVE}.sha256" ]; then
  echo "Checking SHA256 checksum..."
  sha256sum -c "${ARCHIVE}.sha256"
fi

if docker compose ps db 2>/dev/null | grep -q "Up"; then
  # Check if database has tables
  TABLE_COUNT=$(docker compose exec -T db psql -U postgres -d school_admission -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';")
  if [ "${TABLE_COUNT}" -gt 0 ] && [ "${FORCE}" != "--force" ]; then
    echo "ERROR: Target database 'school_admission' contains ${TABLE_COUNT} tables. Pass --force to overwrite."
    exit 1
  fi

  echo "Restoring into database 'school_admission'..."
  docker compose exec -T db pg_restore -U postgres -d school_admission --clean --if-exists < "${ARCHIVE}" || true
else
  DB_HOST="${DB_HOST:-localhost}"
  DB_PORT="${DB_PORT:-5432}"
  DB_USER="${DB_USER:-postgres}"
  DB_NAME="${DB_NAME:-school_admission}"
  PGPASSWORD="${DB_PASSWORD:-postgrespassword}"

  TABLE_COUNT=$(PGPASSWORD="${PGPASSWORD}" psql -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" -d "${DB_NAME}" -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';" 2>/dev/null || echo "0")
  if [ "${TABLE_COUNT}" -gt 0 ] && [ "${FORCE}" != "--force" ]; then
    echo "ERROR: Target database '${DB_NAME}' contains ${TABLE_COUNT} tables. Pass --force to overwrite."
    exit 1
  fi

  echo "Restoring into database '${DB_NAME}'..."
  PGPASSWORD="${PGPASSWORD}" pg_restore -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" -d "${DB_NAME}" --clean --if-exists "${ARCHIVE}" || true
fi

echo "Restore finished successfully!"
