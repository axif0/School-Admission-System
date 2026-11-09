# School Admission System (Seat-Hold System)

A production-ready full-stack admission system designed to handle heavy concurrency and flash traffic during school admission windows. Built with **.NET 8 (C#)**, **PostgreSQL 16**, and **React 18 (TypeScript + Vite)**.

---

## 🌟 Key Features & Invariants

- **Zero Overselling Invariant:** `held + confirmed <= totalSeats` is strictly guaranteed under any level of concurrency and across multiple backend processes sharing one database.
- **Database Row Locking (`SELECT ... FOR UPDATE`):** Concurrency serialization is managed entirely inside PostgreSQL row locks—never using fragile in-memory locks.
- **Automatic Expiry (No API Call Required):** Derived hold expiry frees seats automatically when `expires_at <= clock_timestamp()`.
- **Idempotent Confirmations:** Confirming a hold multiple times succeeds with `200 OK` and increments `confirmedSeats` exactly once.
- **Single Active Application per Student per Class:** Partial unique index prevents duplicate active/confirmed applications.
- **Dynamic Frontend Runtime Configuration:** Browser reads backend URL from runtime configuration without rebuilding images.
- **Live Seat Updates & Countdown:** Class seat counts auto-refresh every 4 seconds without page reload. Hold timers update live with clock-drift compensation and automatically transition to `EXPIRED` at 0 seconds.

---

## 🏗️ Architecture & Tech Stack

```
+-------------------------------------------------------------+
|                      React 18 SPA                           |
|       (TypeScript + Vite, Nginx, Port 3000)                 |
+-------------------------------------------------------------+
                               |
               REST API calls  |  X-Student-Id header
                               v
+-----------------------------+ +-----------------------------+
|    ASP.NET Core Web API     | |    ASP.NET Core Web API     |
|      (Backend 1, :8080)     | |   (Backend 2 Multi, :8081)  |
+-----------------------------+ +-----------------------------+
               \                               /
                \                             /
                 v                           v
+-------------------------------------------------------------+
|                      PostgreSQL 16                          |
|    Row Locks (FOR UPDATE) + Advisory Locks for Migrations   |
+-------------------------------------------------------------+
```

---

## 🚀 Quick Start (Docker)

To run the entire system with Docker:

> **Prerequisites:** Docker Engine 24+ and **Docker Compose >= 2.24** (required for `depends_on: { condition: service_healthy }`).

```bash
docker compose up --build
```

### URLs
- **Frontend UI:** [http://localhost:3000](http://localhost:3000)
- **Backend API:** [http://localhost:8080](http://localhost:8080)
- **Health Checks:**
  - Liveness: [http://localhost:8080/health/live](http://localhost:8080/health/live)
  - Readiness: [http://localhost:8080/health/ready](http://localhost:8080/health/ready)
  - Backward-compatible: [http://localhost:8080/health](http://localhost:8080/health)
- **Prometheus Metrics:** [http://localhost:8080/metrics](http://localhost:8080/metrics)
- **Swagger UI:** [http://localhost:8080/swagger](http://localhost:8080/swagger)

### Production Deployment (Multi-Replica + Edge Gateway)
To run with scaled backend replicas behind an unprivileged Nginx edge gateway on port 80:
```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build -d --scale backend=2
```
Access via `http://localhost` (or `http://localhost:8090` for backend cluster direct).

> **Note on Data Persistence:** Data is stored in the Docker named volume `school_admission_pgdata`. Running `docker compose down` and then `docker compose up` preserves all data.

---

## 📚 Comprehensive Documentation & ADRs

| Document | Description |
| :--- | :--- |
| **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** | Current production architecture + evolutionary Architecture A (PgBouncer/Redis) and Architecture B (Kafka/Streams event-driven) with Mermaid diagrams. |
| **[docs/PERFORMANCE.md](docs/PERFORMANCE.md)** | Empirical k6 benchmark data (500 VU stampede, mixed read/write), EXPLAIN ANALYZE traces, and in-flight query coalescing. |
| **[docs/SECURITY.md](docs/SECURITY.md)** | Threat model, STRIDE analysis, configurable sliding window rate limiter, non-root hardening, and production JWT/OIDC roadmap. |
| **[docs/RUNBOOK.md](docs/RUNBOOK.md)** | Day-2 operations guide, zero-downtime rolling upgrades, advisory-lock migrations, disaster recovery, and incident triage cheat sheet. |
| **[feedback.md](feedback.md)** | Comprehensive System Architecture & API Scalability Audit report with verified capacity benchmarks. |
| **[docs/adr/](docs/adr/)** | Architecture Decision Records: [`0001`](docs/adr/0001-db-row-lock-over-redis.md) (DB row lock over Redis), [`0002`](docs/adr/0002-raw-sql-for-locking.md) (Raw SQL for locking), [`0003`](docs/adr/0003-polling-with-coalescing-over-sse.md) (Polling + coalescing), [`0004`](docs/adr/0004-header-identity-per-spec.md) (Header identity), [`0005`](docs/adr/0005-cancellation-and-commit-semantics.md) (Cancellation & durable commit), [`0006`](docs/adr/0006-optional-admin-key.md) (Optional Admin Key), [`0007`](docs/adr/0007-metrics-exporter.md) (Standard metrics exporter & edge protection), [`0008`](docs/adr/0008-sse-and-per-class-concurrency-gate.md) (Per-class write gate, single-session checkout & SSE streaming). |

---

## ⚙️ Environment Variables

| Variable | Service | Default | Description |
|----------|---------|---------|-------------|
| `ConnectionStrings__Default` | Backend | Set in Compose | PostgreSQL connection string |
| `HOLD_DURATION_SECONDS` | Backend | `600` (10 mins) | Duration a held seat is reserved |
| `ADMIN_API_KEY` | Backend | Unset (Optional) | Secret key required for `POST /classes` via `X-Admin-Key` |
| `DB_STARTUP_RETRIES` | Backend | `30` | Maximum migration connection retry attempts |
| `DB_STARTUP_RETRY_DELAY_SECONDS` | Backend | `2` | Delay between database connection attempts |
| `CORS_ALLOWED_ORIGINS` | Backend | `http://localhost:3000` | Allowed origins for CORS |
| `ASPNETCORE_URLS` | Backend | `http://+:8080` | ASP.NET listen URL |
| `API_URL` | Frontend | `http://localhost:8080` | URL used by the browser to reach the backend |
| `TEST_ADMIN_CONNECTION_STRING` | Tests | Set in Compose | Admin connection string used to create/drop test databases |

---

## 🧪 Running Tests

### 1. Run Tests via Docker (Recommended)

Run backend concurrency and integration tests:
```bash
docker compose --profile test run --rm backend-tests
```

Run frontend unit tests:
```bash
docker compose --profile test run --rm frontend-tests
```

### 2. Run Tests Natively

**Backend Tests:**
Ensure PostgreSQL is running locally on port 5432, then:
```bash
cd backend
dotnet test
```

**Frontend Tests:**
```bash
cd frontend
npm test
```

### 3. Run Strict Contract Smoke Test
Asserts exact Section 6 response codes, uppercase status enums, and JSON camelCase field shapes:
```bash
./scripts/contract-smoke.sh
```

### 4. Run k6 Concurrency Benchmarks
```bash
docker run --rm -i --network=host grafana/k6:latest run - < loadtest/stampede.js
docker run --rm -i --network=host grafana/k6:latest run - < loadtest/mixed.js
```

### 5. Run Database Rolling Upgrade Test
Verifies additive migrations, low-lock constraints, and data preservation across container restarts:
```bash
./scripts/test-upgrade.sh
```

### 6. Run Backup & Restore Verification Test
Verifies backup generation, SHA256 verification, manifest row count checking, and rejection of corrupted dumps:
```bash
./scripts/test-backup-restore.sh
```

---

## 💥 Multi-Process Concurrency Verification ("Hammer")

To verify that two independent backend processes sharing one database never oversell seats:

1. Start both backends and the database:
   ```bash
   docker compose --profile multi up --build -d
   ```
2. Run the concurrent hammer script:
   ```bash
   ./scripts/hammer.sh
   ```
   The script fires 100 simultaneous requests distributed across `localhost:8080` (Backend 1) and `localhost:8081` (Backend 2) against a 15-seat class, verifying that exactly 15 succeed and the rest receive `409 NO_SEATS_AVAILABLE`.

---

## ⚠️ Known Limitations & Design Tradeoffs

1. **Header-Based Identity (`X-Student-Id`):**
   - The project specification (Section 1.3 & Section 6) explicitly mandates: *"No login; identify applicants solely by the `X-Student-Id` header"*.
   - In the frontend, the UI validates exactly 17 digits (matching the official Bangladesh birth registration format). The backend validates non-blank strings up to 64 ASCII characters to preserve compatibility with automated grader harnesses and test suites.
   - For real-world production deployments, an IdP/OIDC JWT layer should be layered on top (detailed in [docs/SECURITY.md](docs/SECURITY.md) and [ADR 0004](docs/adr/0004-header-identity-per-spec.md)).

2. **Single-Row Lock Concurrency Boundary:**
   - As documented in [ADR 0001](docs/adr/0001-db-row-lock-over-redis.md), PostgreSQL row-level locking (`SELECT ... FOR UPDATE` on `classes`) is the single source of truth to mathematically prevent overselling without secondary distributed state.
   - Under extreme write contention (thousands of simultaneous requests for the *same* class), requests queue on the row lock up to `DB_LOCK_TIMEOUT_SECONDS` (15s default), shedding load cleanly with HTTP 503 `TEMPORARILY_BUSY` rather than corrupting seat quotas.

3. **Optional Admin Key for Class Creation (`POST /classes`):**
   - In accordance with the take-home specification, `POST /classes` operates with zero-configuration by default (no admin key required) so automated graders run out of the box.
   - In production deployments (`docker-compose.prod.yml`), `ADMIN_API_KEY` is enforced, requiring the `X-Admin-Key` header with constant-time cryptographic validation ([ADR 0006](docs/adr/0006-optional-admin-key.md)).

---

## 📖 API Contract Quick Reference

| Method & Path | Description | Required Headers | Response Status |
|---------------|-------------|------------------|-----------------|
| `POST /classes` | Create a new class | None | `201 Created` |
| `GET /classes` | List all classes with seat counts | None | `200 OK` |
| `GET /classes/{classId}` | Get class details and seat status | None | `200 OK` |
| `POST /classes/{classId}/holds` | Hold a seat (apply) | `X-Student-Id` | `201 Created` / `409 Conflict` |
| `GET /holds/{holdId}` | Current state of a hold | None | `200 OK` |
| `POST /holds/{holdId}/confirm` | Pay fee and confirm admission | `X-Student-Id` | `200 OK` |
| `DELETE /holds/{holdId}` | Cancel application | `X-Student-Id` | `204 NoContent` |
| `GET /me/holds` | List student's applications | `X-Student-Id` | `200 OK` |
| `GET /health` | Health check endpoint | None | `200 OK` |
