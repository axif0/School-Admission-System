# Operational & Testing Scripts

This directory contains shell automation scripts for testing, benchmarking, backups, and operations.

| Script | Purpose | Example Usage |
| :--- | :--- | :--- |
| `hammer.sh` | Concurrency load test hammering a class with parallel curl requests. | `./scripts/hammer.sh http://localhost:8080 c_123 100` |
| `contract-smoke.sh` | Strict verification of all 8 Section 6 endpoints, statuses, and field shapes. | `./scripts/contract-smoke.sh` |
| `backup.sh` | Creates compressed PostgreSQL dump (`pg_dump -Fc`) with SHA256 checksum. | `./scripts/backup.sh ./backups` |
| `restore.sh` | Validates checksum and restores database archive with overwrite safety checks. | `./scripts/restore.sh ./backups/school_admission_xxx.dump --force` |
