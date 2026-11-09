# Operations & Reliability Runbook

This document defines standard operating procedures (SOP), day-2 maintenance, incident triage, and disaster recovery for the **School Admission System**.

---

## 1. Architecture Quick Reference

- **Backend:** ASP.NET Core 8 (.NET 8 Minimal API, running as unprivileged user UID `1654`).
- **Database:** PostgreSQL 16 Alpine (`school_admission`), connection pooling managed by Npgsql with lock timeouts.
- **Frontend:** React 18 SPA served via unprivileged Nginx (`nginxinc/nginx-unprivileged:alpine`, UID `101`).
- **Edge Gateway (Prod):** Nginx reverse proxy routing `/api/` to backend upstream and `/` to frontend.
- **Single Source of Truth:** PostgreSQL row locks (`SELECT ... FOR UPDATE`) synchronize seat allocations and active hold counts.

---

## 2. Zero-Downtime Deployment & Migration Policy

To achieve zero downtime while rolling out updates across multiple backend instances:

### 2.1 Schema Migration Principles (Expand / Contract)
Direct destructive changes (`ALTER TABLE DROP COLUMN`, changing column types, removing constraints) are strictly forbidden in production.

1. **Phase 1: Expand (Additive):**
   - Add new columns, indexes, or nullable fields (`ALTER TABLE ... ADD COLUMN ...`).
   - If an index is added in production, use `CREATE INDEX CONCURRENTLY` to avoid locking writes.
   - Run migrations on startup; `DatabaseMigrator` automatically acquires a session advisory lock (`pg_advisory_lock(123456789)`), ensuring only one backend replica runs migrations while others wait.
2. **Phase 2: Dual-Writing / Backward-Compatible Code:**
   - Deploy new backend version that reads from old/new columns and writes to both.
3. **Phase 3: Contract (Cleanup):**
   - Once all traffic has migrated to the new version and old replicas are terminated, drop legacy columns or unused tables in a scheduled maintenance window.

### 2.2 Rolling Update Procedure
1. Verify database readiness:
   ```bash
   curl -f http://<host>:8080/health/ready
   ```
2. Build and tag new container images.
3. Perform rolling restart of backend replicas:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --no-deps --build backend
   ```
4. Verify `/health/live` and `/health/ready` on updated instances.

---

## 3. Rollback Playbook

When an incident occurs immediately following a release:

### 3.1 Code Rollback (Stateless)
Because all migrations follow the **Expand/Contract** model, the previous backend version is 100% compatible with the expanded database schema.
1. Revert Git tag or container image to previous release:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --no-deps backend
   ```
2. Verify system health:
   ```bash
   ./scripts/contract-smoke.sh
   ```

### 3.2 Database Rollback (Stateful)
If a faulty migration corrupted data:
1. Stop backend traffic:
   ```bash
   docker compose stop backend
   ```
2. Restore to clean state from pre-deployment backup:
   ```bash
   ./scripts/restore.sh ./backups/school_admission_pre_deploy.dump --force
   ```
3. Restart backend instances and run smoke verification.

---

## 4. Disaster Recovery & Backup SOP

- **Recovery Point Objective (RPO):** < 1 hour (daily automated snapshots + continuous WAL archiving in production).
- **Recovery Time Objective (RTO):** < 15 minutes.

### 4.1 Scheduled Backups
Run `scripts/backup.sh` via cron or orchestration pipeline:
```bash
./scripts/backup.sh /var/backups/school_admission
```
This produces:
- A custom compressed PostgreSQL dump (`.dump`) using `pg_dump -Fc`.
- A SHA256 checksum file (`.dump.sha256`).

### 4.2 Restoring from Backup
```bash
./scripts/restore.sh /var/backups/school_admission/school_admission_20261108_120000.dump --force
```

---

## 5. Incident Triage Cheat Sheet

| Symptom | Probable Cause | Immediate Diagnostic Command | Remediation Action |
| :--- | :--- | :--- | :--- |
| **HTTP 503 `TEMPORARILY_BUSY`** | Lock timeout (lock wait > 15s) under high write contention. | `SELECT pid, age(clock_timestamp(), query_start), usename, query, state FROM pg_stat_activity WHERE state != 'idle';` | 1. Check if a long transaction is blocking `classes` row.<br>2. Terminate hung query: `SELECT pg_terminate_backend(<pid>);`. |
| **HTTP 503 `Database migrations in progress`** | Migration lock held or instance starting up. | `SELECT * FROM pg_locks WHERE locktype = 'advisory';` | Wait up to 30s. If stuck indefinitely, check migration logs and release advisory lock. |
| **Npgsql Connection Pool Exhaustion** | Pool limit reached due to connection leaks or slow queries. | Inspect `metrics` counter and run `SELECT count(*), state FROM pg_stat_activity GROUP BY state;` | Increase `DB_MAX_POOL_SIZE` or verify connection disposal across all endpoints. Ensure single connection per fast path. |
| **HTTP 429 `TOO_MANY_REQUESTS`** | Rate limiting threshold exceeded (burst student holds or DDoS). | Review response header `Retry-After: 60`. Check Prometheus metric `holds_rejected_total{reason="RATE_LIMIT"}`. | If legitimate spike, adjust `RATE_LIMIT_HOLDS_PER_MINUTE_PER_STUDENT` in environment. |
| **Stale Holds Lingering in DB** | Sweeper disabled or interval too long. | `SELECT count(*) FROM holds WHERE status = 'HELD' AND expires_at < now();` | Note: Derived query handles correctness automatically. Enable sweeper via `SWEEPER_ENABLED=true` or trigger manual cleanup. |
