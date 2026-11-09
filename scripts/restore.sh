#!/usr/bin/env bash
set -euo pipefail

ARCHIVE="${1:-}"

if [ -z "${ARCHIVE}" ] || [ ! -f "${ARCHIVE}" ]; then
  echo "Usage: $0 <path_to_dump> [--in-place] [--yes]"
  echo "Default action: safely restores into scratch database 'school_admission_restore_test', verifies table counts match manifest, and drops scratch db."
  echo "Pass --in-place --yes to overwrite live production database 'school_admission'."
  exit 1
fi

IN_PLACE=false
CONFIRM_YES=false

for arg in "$@"; do
  if [ "$arg" = "--in-place" ]; then
    IN_PLACE=true
  fi
  if [ "$arg" = "--yes" ]; then
    CONFIRM_YES=true
  fi
done

echo "Verifying archive: ${ARCHIVE}"

# 1. Check SHA256 if .sha256 exists
if [ -f "${ARCHIVE}.sha256" ]; then
  echo "Verifying SHA256 checksum..."
  ARCHIVE_DIR=$(dirname "${ARCHIVE}")
  ARCHIVE_NAME=$(basename "${ARCHIVE}")
  if ! (cd "${ARCHIVE_DIR}" && sha256sum -c "${ARCHIVE_NAME}.sha256"); then
    echo "ERROR: SHA256 checksum verification failed for ${ARCHIVE}!"
    exit 1
  fi
  echo "Checksum OK."
fi

# Determine execution environment
USE_DOCKER=false
if docker compose ps db 2>/dev/null | grep -q "Up"; then
  USE_DOCKER=true
fi

DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_USER="${DB_USER:-postgres}"
PGPASSWORD="${DB_PASSWORD:-postgrespassword}"

run_sql() {
  local target_db="$1"
  local sql="$2"
  if [ "${USE_DOCKER}" = true ]; then
    docker compose exec -T db psql -U postgres -d "${target_db}" -tAc "${sql}"
  else
    PGPASSWORD="${PGPASSWORD}" psql -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" -d "${target_db}" -tAc "${sql}"
  fi
}

run_restore() {
  local target_db="$1"
  local extra_flags="${2:-}"
  if [ "${USE_DOCKER}" = true ]; then
    docker compose exec -T db pg_restore -U postgres -d "${target_db}" ${extra_flags} < "${ARCHIVE}"
  else
    PGPASSWORD="${PGPASSWORD}" pg_restore -h "${DB_HOST}" -p "${DB_PORT}" -U "${DB_USER}" -d "${target_db}" ${extra_flags} "${ARCHIVE}"
  fi
}

verify_manifest() {
  local target_db="$1"
  if [ -f "${ARCHIVE}.counts" ]; then
    echo "Verifying table row counts against manifest..."
    local exp_classes exp_holds exp_mig
    exp_classes=$(grep -E '^classes=' "${ARCHIVE}.counts" | cut -d= -f2)
    exp_holds=$(grep -E '^holds=' "${ARCHIVE}.counts" | cut -d= -f2)
    exp_mig=$(grep -E '^__EFMigrationsHistory=' "${ARCHIVE}.counts" | cut -d= -f2)

    local act_classes act_holds act_mig
    act_classes=$(run_sql "${target_db}" "SELECT count(*) FROM classes;")
    act_holds=$(run_sql "${target_db}" "SELECT count(*) FROM holds;")
    act_mig=$(run_sql "${target_db}" "SELECT count(*) FROM \"__EFMigrationsHistory\";")

    echo "Manifest verification: classes=${act_classes}/${exp_classes}, holds=${act_holds}/${exp_holds}, migrations=${act_mig}/${exp_mig}"

    if [ "${act_classes}" != "${exp_classes}" ] || [ "${act_holds}" != "${exp_holds}" ] || [ "${act_mig}" != "${exp_mig}" ]; then
      echo "ERROR: Manifest count mismatch! Expected (classes=${exp_classes}, holds=${exp_holds}, migrations=${exp_mig}), got (${act_classes}, ${act_holds}, ${act_mig})"
      exit 1
    fi
    echo "Manifest row counts match perfectly!"
  fi
}

if [ "${IN_PLACE}" = true ]; then
  if [ "${CONFIRM_YES}" != true ]; then
    echo "ERROR: --in-place requires --yes confirmation to overwrite live production database."
    exit 1
  fi

  echo "WARNING: Overwriting database 'school_admission' in place..."
  run_restore "school_admission" "--clean --if-exists"
  verify_manifest "school_admission"
  echo "In-place restore completed successfully!"
else
  # Default: Scratch database restore test
  SCRATCH_DB="school_admission_restore_test"
  echo "Performing safe verification restore into scratch database '${SCRATCH_DB}'..."

  # Create scratch database
  run_sql "postgres" "DROP DATABASE IF EXISTS ${SCRATCH_DB};" >/dev/null
  run_sql "postgres" "CREATE DATABASE ${SCRATCH_DB};" >/dev/null

  # Restore into scratch database
  run_restore "${SCRATCH_DB}" ""

  # Verify counts
  verify_manifest "${SCRATCH_DB}"

  # Clean up scratch database
  run_sql "postgres" "DROP DATABASE ${SCRATCH_DB};" >/dev/null
  echo "Scratch database verification succeeded! Archive is completely sound and valid."
fi
