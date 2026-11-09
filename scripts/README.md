# Operational & Testing Scripts

This directory contains shell automation scripts for testing, benchmarking, backups, and operations.

| Script | Purpose | Example Usage |
| :--- | :--- | :--- |
| `hammer.sh` | Concurrency load test hammering a class with parallel curl requests across multiple backend replicas. | `./scripts/hammer.sh http://localhost:8080 c_123 100` |
| `contract-smoke.sh` | Strict verification of all Section 6 endpoints, status codes, headers, and JSON camelCase field shapes. | `./scripts/contract-smoke.sh` |
| `backup.sh` | Creates compressed PostgreSQL dump (`pg_dump -Fc`) with SHA256 checksum and `.counts` row manifest. | `./scripts/backup.sh ./backups` |
| `restore.sh` | Safely validates checksum, restores into a scratch database, verifies manifest counts, and drops scratch db (or restores in-place with `--in-place --yes`). | `./scripts/restore.sh ./backups/school_admission_xxx.dump` |
| `test-backup-restore.sh` | Automated verification test that seeds data, takes backup, corrupts a copy to verify rejection, and verifies scratch restore. | `./scripts/test-backup-restore.sh` |
| `test-upgrade.sh` | Automated rolling upgrade test verifying additive migration and zero-downtime data preservation across container restarts. | `./scripts/test-upgrade.sh` |
