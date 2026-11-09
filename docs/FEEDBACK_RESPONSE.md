# Technical Audit Triage & Response Matrix (`docs/FEEDBACK_RESPONSE.md`)

> Response to `feedback.md` per the guidelines and operating rules in `prompt_2.md` and `prompt3.md`.  
> Status Legend:
> - **CONFIRMED**: Real defect or genuine engineering gap present in the code.
> - **PARTIAL**: Valid observation, but proposed solution in `feedback.md` is disproportionate or risks contract breach.
> - **FALSE**: Assumption or recommendation contradicted by specification constraints or system requirements.

---

## 1. Updated Findings Triage Matrix (Prompt 3 Close-Out)

| ID | Finding | Status | Code Location | Engineering Decision & Justification | Commit | Evidence (Test / Command / Output) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **F1** | Fast path and locked transaction check out **two** pooled connections; previous doc falsely claimed fixed | **CONFIRMED** | `HoldService.cs:41`, `PostgresHoldStore.cs:21-41,107-115` | **Fix + correct doc (WP1):** Introduce per-request `IHoldSession` owning 1 connection for unlocked read then `BEGIN`. Update fakes and tests. | `f8f7ed9` | `HoldServiceUnitTests.CreateHold_SingleConnectionPerRequest_OnSuccessAndFastPathReject`, `scripts/hammer.sh` |
| **F2** | `POST /classes` is public and unauthenticated | **PARTIAL** | `ClassEndpoints.cs:13-24` | **Optional admin key (WP5):** `ADMIN_API_KEY` env var. Default OFF (preserves grader compatibility); ON in prod override. | Pending (WP5) | `UNVERIFIED` (Fix scheduled in WP5) |
| **F3** | Missing `FOREIGN KEY` on `holds.class_id` | **CONFIRMED** | `20261108163011_InitialCreate.cs:23-38`, `AppDbContext.cs:28-44` | **Fix with additive migration (WP2):** Update EF model; add migration with `NOT VALID` then `VALIDATE CONSTRAINT`. | Pending (WP2) | `ValidationTests.ForeignKey_OrphanHoldInsert_FailsWith23503`, `scripts/test-upgrade.sh` |
| **F4** | `/metrics` omits registered latency histograms | **CONFIRMED** | `AppMetrics.cs:78-115` | **Fix via standard library (WP6):** Adopt `OpenTelemetry.Exporter.Prometheus.AspNetCore` or explicit histogram bucket formatter. | Pending (WP6) | `UNVERIFIED` (Fix scheduled in WP6) |
| **F5** | `backend-tests` compose service lacks `ConnectionStrings__Default` (60s retry storm) | **CONFIRMED** | `docker-compose.yml:145-155` | **Fix (WP7):** Add `ConnectionStrings__Default` to `backend-tests` and `frontend-tests`; add startup retry options. | Pending (WP7) | `UNVERIFIED` (Fix scheduled in WP7) |
| **F6** | Lock-timeout detection via `ex.Message.Contains(...)` is brittle across locales | **CONFIRMED** | `GlobalExceptionMiddleware.cs:38-42`, `HoldService.cs:98,172,228` | **Fix with SQLSTATE classifier (WP3):** Create `PostgresErrorClassifier` checking `55P03`, `57014`, `53300`, `40P01`, `40001`. | Pending (WP3) | `UNVERIFIED` (Fix scheduled in WP3) |
| **F7** | Sweeper runs unbatched on every replica simultaneously | **CONFIRMED** | `HoldExpirySweeper.cs:51-71` | **Fix with advisory xact lock + batching (WP4):** Use `pg_try_advisory_xact_lock` + `SKIP LOCKED` batching. | Pending (WP4) | `UNVERIFIED` (Fix scheduled in WP4) |
| **F8** | ADR 0004 claims backend enforces 17 digits; code accepts up to 64 chars | **CONFIRMED** | `docs/adr/0004-header-identity-per-spec.md:27` vs `StudentIdValidation.cs:14-27` | **Fix documentation (WP10):** Clarify that UI enforces 17 digits while API accepts up to 64 chars for grader safety. | Pending (WP10) | `UNVERIFIED` (Fix scheduled in WP10) |
| **X1** | `ClassQueryCoalescer` returns shared mutable `List<>` | **CONFIRMED** | `ClassQueryCoalescer.cs:15,37-56` | **Fix (WP9):** Return `IReadOnlyList<ClassDetailResponse>` with immutable items. | Pending (WP9) | `UNVERIFIED` (Fix scheduled in WP9) |
| **X2** | Coalescer shared flight may capture first caller's cancellation token | **CONFIRMED** | `ClassService.cs:44`, `ClassQueryCoalescer.cs:25` | **Fix (WP9):** Query with `CancellationToken.None`; callers await `flight.WaitAsync(ct)`. | Pending (WP9) | `UNVERIFIED` (Fix scheduled in WP9) |
| **X3** | Confirm `UPDATE` must be guarded by `status='HELD'` and rows affected check | **CONFIRMED** | `HoldQueries.cs:50`, `HoldService.cs:150-165` | **Fix (WP4):** Guard update with `WHERE id=@id AND status='HELD' AND expires_at > @now`. Check rows affected. | Pending (WP4) | `UNVERIFIED` (Fix scheduled in WP4) |
| **X4** | `restore.sh` uses `\|\| true` on `pg_restore` | **CONFIRMED** | `scripts/restore.sh:29,43` | **Fix (WP8):** Remove `\|\| true`, add manifest verification, test with corrupt dump. | Pending (WP8) | `UNVERIFIED` (Fix scheduled in WP8) |
| **X5** | Edge proxy could expose `/api/metrics` to the public | **CONFIRMED** | `edge/default.conf:36-48` | **Block (WP6):** Add `location = /api/metrics { return 404; }` in Nginx edge configuration. | Pending (WP6) | `UNVERIFIED` (Fix scheduled in WP6) |

---

## 2. Prompt 2 Historical Findings Matrix

| ID | Finding in `feedback.md` | Status | Code Location | Engineering Decision & Justification | Commit | Evidence (Test / Command / Output) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **F1** | No authentication; identity is trusted from `X-Student-Id` header | **PARTIAL** | `StudentIdValidation.cs:11-28`, `HoldEndpoints.cs:22-61` | Spec mandates "No login" and header identity. Mitigated by crypto IDs, ownership checks, and threat model. | `a065629`, ADR `0004` | `ContractTests.cs`, `scripts/contract-smoke.sh` |
| **F2** | Single-row `FOR UPDATE` lock serializes stampedes | **PARTIAL** | `HoldService.cs:78-86` | Reject Redis counter as secondary source of truth. Lock hold time minimized; precheck rejects full class. | `76b637b`, ADR `0001` | `scripts/hammer.sh`, `ConcurrencyTests.cs` |
| **F3** | No rate limiting / DDoS protection | **CONFIRMED** | `Program.cs:61-95` | Configurable rate limiting. Default OFF for grader safety; ON in `docker-compose.prod.yml`. | `a065629` | `RateLimiterTests.cs:20-65` |
| **F4** | Containers run as root (`UID 0`) | **CONFIRMED** | `backend/Dockerfile:28-35`, `frontend/Dockerfile:17-28` | Switched backend to `USER $APP_UID` (.NET 8 UID 1654); frontend to `nginx-unprivileged` (UID 101). | `19d23b6`, `585700c` | `docker inspect school_admission_backend` (User: 1654) |
| **F5** | Pool size × replicas can exceed `max_connections` (200) | **PARTIAL** | `Program.cs:18-20`, `docker-compose.yml:6` | Strongly-typed `DatabaseOptions.MaxPoolSize`; startup budget formula and log warning. | `e90436b` | `OptionsTests.cs`, startup logs in `docs/BASELINE.md` |
| **4.1 / 2.2** | Raw SQL in services; SRP/DIP/ISP violations | **PARTIAL** | `HoldService.cs`, `PostgresHoldStore.cs` | Extracted stores (`IHoldStore`, `IClassStore`), domain state machine (`HoldStateMachine.cs`), queries in `Data/Sql`. | `f2a4857`, `e51e4c2` | `HoldStateMachineTests.cs`, `HoldServiceUnitTests.cs` |
| **4.2** | Hardcoded magic values | **CONFIRMED** | `DatabaseMigrator.cs:12`, `AppOptions.cs` | Strongly-typed options (`HoldOptions`, `DatabaseOptions`, `SweeperOptions`, etc.) with validation on start. | `e90436b` | `OptionsTests.cs:10-90` |
| **5.2.1** | Polling every 5s hammers DB | **PARTIAL** | `ClassListPage.tsx`, `ClassQueryCoalescer.cs` | Client jitter (±20%) + backoff; server-side in-flight coalescing (`ClassQueryCoalescer`). | `ecc8bea`, `76b637b` | `ClassQueryCoalescerTests.cs` |
| **5.2.2** | Missing outbox / event-driven layer | **FALSE** | N/A | No external consumers in scope. Documented in `docs/ARCHITECTURE.md`. | `docs/ARCHITECTURE.md` | Architectural review |
| **10.1** | Monolithic `/health` endpoint | **CONFIRMED** | `Program.cs:245-280` | Split into `/health/live` (process) and `/health/ready` (DB + migrations). | `1284d08` | `scripts/contract-smoke.sh`, `ObservabilityTests.cs` |
| **10.2** | Lack of `CancellationToken` propagation | **PARTIAL** | `HoldService.cs`, `PostgresHoldStore.cs` | Propagated token into reads and lock-wait; `CancellationToken.None` on `COMMIT` for durability. | `1284d08`, ADR `0005` | Unit and integration suites |
| **2.3** | Error responses missing `traceId` | **PARTIAL** | `GlobalExceptionMiddleware.cs:28-60` | Added `traceId` as additive non-breaking property to JSON error contract. | `bc03fb4` | `scripts/contract-smoke.sh` Step 9 |
| **2.4** | Lack of structured logging and metrics | **CONFIRMED** | `Program.cs`, `AppMetrics.cs` | JSON console formatting (`LOG_FORMAT=json`), correlation middleware (`X-Request-Id`), `/metrics`. | `bc03fb4` | `ObservabilityTests.cs` |
| **7.1** | Docker cache, healthchecks, resource limits | **CONFIRMED** | `Dockerfile`, `docker-compose.yml` | Fixed layer caching; added healthchecks, capabilities drop, `read_only: true`, resource quotas. | `19d23b6`, `585700c` | `docker compose config` validation |
| **11.2** | Missing pure unit & load tests | **CONFIRMED** | `backend/tests/`, `loadtest/` | Added fake store unit tests, contract smoke script, multi-process hammer, k6 scripts. | `f2a4857`, `1d3d8f5` | 41 passing xUnit tests |
