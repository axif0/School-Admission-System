# PROMPT 2: Harden & Polish the School Admission System (response to `feedback.md`)

> **Inputs in the repo:** the finished project, `prompt.md` (the original brief, **still binding**), and `feedback.md` (an audit of the project).
> **Tell your AI coding tool:** *"Read prompt_2.md fully. Work through the work packages (WP) in order. After each WP run the verification gate, report the results, and wait for my go-ahead."*

**Goal:** close the real gaps in `feedback.md` and show senior-level judgment, **without breaking the assignment contract and without over-engineering.**
Honest expectation: no prompt can guarantee a score. What this prompt does is remove every *verified* weakness, avoid changes that could fail the graders' automated tests, and document every trade-off so a reviewer sees deliberate engineering decisions.

---

## 0. Operating Rules (read first)

1. **Contract freeze.** Section 6 of `prompt.md` (the 8 endpoints, headers, JSON shapes, `201/200/204/409`) must not change. Graders run automated tests directly against the API. After every WP, all of these must pass: backend tests, frontend tests, `scripts/hammer.sh`, and the contract smoke test (WP8).
2. **Verify before fixing.** `feedback.md` was machine-generated. For each finding, open the cited code and classify it **CONFIRMED / PARTIAL / FALSE** with `file:line` evidence before touching anything. Do not "fix" things that are not broken.
3. **Measure before and after.** `feedback.md` contains throughput and CPU numbers (e.g., "85% CPU", "P99 ~800ms") that are **estimates, not measurements**. Do not repeat them anywhere. Every performance claim in the docs must come from a real k6 run, recorded with command, hardware, and date in `docs/PERFORMANCE.md`.
4. **Zero-config default stays.** A clean clone must still work with only `docker compose up --build` and no `.env` file. Production hardening goes into `docker-compose.prod.yml` (an override file), not into the default path.
5. **No new dependencies** unless this file names them. Prefer .NET / nginx / shell built-ins.
6. **One logical change per commit**, Conventional Commits, with the finding ID in the message, e.g. `fix(docker): run backend as non-root (F4)`.
7. **Never fabricate.** No invented benchmark results, no invented incidents in `AI_USAGE.md`. If something cannot be verified, say so.
8. **Stop at each gate.** Do not start the next WP while anything is failing.

---

## 1. Triage of `feedback.md` (decisions already made; follow them)

| ID | Finding in `feedback.md` | Decision | Why |
|----|--------------------------|----------|-----|
| F1 | No auth; identity is the `X-Student-Id` header (JWT/OTP recommended) | **Do NOT implement JWT/OTP. Mitigate within the contract and document the threat model** (WP6, WP10) | The assignment **mandates** header identity and "no login", and graders call the API with that header. Adding tokens would break every automated test. The senior move is a documented threat model plus a production design sketch. |
| F2 | Single-row `FOR UPDATE` lock serializes a stampede (Redis Lua gatekeeper, bucket sharding recommended) | **Adapt:** measure first, shorten lock hold time, keep the DB as the single source of truth. **Reject Redis/sharding.** (WP5) | The lock is **per class**, and the existing fast path rejects "full" requests without the lock, so contention is bounded by the seat count. A Redis counter becomes a second source of truth that can leak or drift and could cause overselling or ghost seats. The brief also requires correctness across two processes on one DB. |
| F3 | No rate limiting | **Implement, configurable. Default OFF in default compose; ON in prod override.** (WP6) | A default per-IP limit would 429 the graders' parallel-request tests. |
| F4 | Containers run as root | **Accept** (WP1) | Cheap, standard, no contract risk. |
| F5 | Pool size × replicas can exceed `max_connections`; use PgBouncer | **Adapt:** env-driven pool size + startup budget check + docs. PgBouncer only as optional profile with caveats. (WP2, WP10) | PgBouncer **transaction mode breaks session-level `pg_advisory_lock`** used by the migrator unless the migrator connects directly. Optional, not default. |
| F6 | Fast path checks out two connections | **Accept** (verify first) (WP3) | Easy win. |
| 4.1/2.2 | Raw SQL in services; SRP/DIP/ISP violations; "bypasses EF" | **Adapt:** introduce interfaces, isolate SQL in one data-access layer, pure domain state machine; keep raw SQL for locking and **write an ADR** explaining why. **No EF rewrite.** (WP4) | Row locks, `clock_timestamp()` and partial-index semantics are clearer in SQL. The weakness is the *mixing*, not the SQL. |
| 4.2 | Magic values (lock timeout, sweeper interval, advisory key) | **Accept** (WP2) | |
| 5.2.1 | Polling 5 s hammers the DB (Redis cache / SSE recommended) | **Adapt:** client jitter + backoff, server-side **request coalescing** for `GET /classes`. **Reject TTL cache and Redis.** SSE stays documented future work. (WP5) | A TTL cache breaks read-your-writes (a test that creates a hold then reads `/classes` would see stale counts). Coalescing shares only *in-flight* queries. |
| 5.2.2 | Missing outbox / event layer | **Reject** (document) | No notifications, invoices, or payments exist in scope. |
| 10.1 | Single `/health` | **Accept:** add `/health/live` and `/health/ready`, keep `/health` (WP3) | |
| 10.2 | No `CancellationToken` propagation | **Adapt** (WP3) | Postgres already rolls back an aborted transaction. Correct handling needs nuance (see WP3). |
| 2.3 | Not RFC 7807, no `traceId` | **Adapt:** additive fields only (WP7) | The brief does not define an error body; additive fields are safe. |
| 2.4 | No structured logs / metrics / tracing | **Accept** built-in JSON logs + `System.Diagnostics.Metrics`; Prometheus endpoint optional (WP7) | |
| 7.1 | Dockerfile restore-cache, no HEALTHCHECK, no limits | **Accept** (WP1) | |
| 11.2 | No unit tests, no E2E, no load tests | **Accept** (WP8) | |
| 12 | Redis, read replicas, CDN, waiting room, Kubernetes | **Reject for implementation; document as "Architecture B, future"** (WP10) | Out of proportion for this system; adding them would add risk, not credit. |

---

## 2. Work Packages

### WP0: Baseline & finding verification
1. On a clean checkout run: backend tests, frontend tests, `docker compose up --build`, `scripts/hammer.sh`. Record results in `docs/BASELINE.md` (commands + outcomes). Everything must be green before changes.
2. Create `docs/FEEDBACK_RESPONSE.md` with one row per finding (F1…): status (CONFIRMED/PARTIAL/FALSE), evidence (`file:line`), decision (from Section 1), and later the commit hash that resolved it. **Fill it as you go.**
3. **Gate:** baseline green; table started.

---

### WP1: Container & Compose hardening (F4, Dockerfile findings)

**Backend `Dockerfile`**
- Restore-layer caching: copy only `*.sln`, `*.csproj`, `Directory.Build.props`, `nuget.config` first, run `dotnet restore`, **then** copy sources.
- Run unprivileged and set production env:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SchoolAdmission.Api.dll"]
```
- Pin base image tags (e.g., `8.0` → a specific minor tag where practical). Keep the `test` stage.

**Frontend `Dockerfile` + nginx**
- Final stage `nginxinc/nginx-unprivileged:alpine` (listens on **8080** inside the container; update compose to `3000:8080`).
- Runtime config without root or a writable web root: write `config.js` to `/tmp` from an entrypoint hook `frontend/docker-entrypoint.d/40-runtime-config.sh` (the official image runs scripts in `/docker-entrypoint.d/`):
```sh
#!/bin/sh
set -eu
API_URL="${API_URL:-http://localhost:8080}"
esc=$(printf '%s' "$API_URL" | sed 's/\\/\\\\/g; s/"/\\"/g')
printf 'window.__APP_CONFIG__ = { API_URL: "%s" };\n' "$esc" > /tmp/config.js
```
- nginx config: `server_tokens off;`, gzip for text assets, SPA fallback, and caching rules:
```nginx
location = /config.js   { alias /tmp/config.js; add_header Cache-Control "no-store"; }
location = /index.html  { add_header Cache-Control "no-cache"; }
location /assets/       { expires 1y; add_header Cache-Control "public, immutable"; }
location /              { try_files $uri /index.html; }
```
- Add security headers on all responses: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, and a `Content-Security-Policy` that allows only `'self'` plus the configured API origin in `connect-src` (verify the UI still works).

**`docker-compose.yml` (default, still zero-config)**
- Every service: `restart: unless-stopped`, `init: true`, log rotation (`json-file`, `max-size: 10m`, `max-file: 3`).
- Backend: `stop_grace_period: 30s`, `cap_drop: [ALL]`, `security_opt: ["no-new-privileges:true"]`, `read_only: true` with `tmpfs: [/tmp]` (verify the app still starts), and a healthcheck. The aspnet image has `bash` but no `curl`, so use a TCP/HTTP probe:
```yaml
healthcheck:
  test: ["CMD", "bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/ready HTTP/1.0\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]
  interval: 10s
  timeout: 3s
  retries: 5
  start_period: 20s
```
- Frontend: same hardening (`read_only`, `tmpfs: [/tmp, /var/cache/nginx, /var/run]`), healthcheck via `wget -q -O /dev/null http://127.0.0.1:8080/` (busybox wget exists in alpine).
- `db`: do **not** publish 5432 to the host by default (or bind `127.0.0.1:5432` only), keep the named volume and `pg_isready` healthcheck, keep `max_connections=200`.
- Resource limits/reservations in a `deploy.resources` block for each service (e.g., backend `cpus: "1.0"`, `memory: 512M`) with values documented as starting points to tune after load tests.
- Credentials stay overridable defaults (`${POSTGRES_PASSWORD:-postgres}`) so zero-config works; `.env.example` documents them.
- `frontend` `depends_on: backend (service_healthy)`.

**`docker-compose.prod.yml` (override: dev/prod separation)**
Used as `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d`. It must:
- Require real secrets: `${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD}` (fails fast if unset).
- Set `RATE_LIMIT_ENABLED=true`, `SWAGGER_ENABLED=false`, `ASPNETCORE_ENVIRONMENT=Production`, strict `CORS_ALLOWED_ORIGINS`, `METRICS_ENABLED=true` (internal only).
- Add an **`edge` nginx service** that is the only published port: serves the SPA, proxies `/api/` to the `backend` service (Docker DNS), and supports `--scale backend=2` (**Architecture A**). Frontend `API_URL=/api` in this mode. Document that TLS terminates in front of `edge` (or add a commented TLS server block).
- Do not publish the DB or backend ports.

**Gate:** `docker compose up --build` works from a clean clone with no `.env`; `docker compose ps` shows all services healthy; `docker compose exec backend id` shows a non-root uid; UI loads on `:3000` and all flows work; `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --scale backend=2` works and `scripts/hammer.sh` (pointed at the edge) still shows `held + confirmed <= totalSeats`.

---

### WP2: Configuration via Options (F5, magic values)

- Create strongly typed, validated options (`ValidateOnStart`): `HoldOptions` (`HOLD_DURATION_SECONDS`, default 600), `DatabaseOptions` (`DB_LOCK_TIMEOUT_SECONDS` default 15, `DB_MAX_POOL_SIZE` default 50, `MIGRATION_ADVISORY_LOCK_KEY` default stays but is now configurable), `SweeperOptions` (`SWEEPER_INTERVAL_SECONDS` default 5, `SWEEPER_ENABLED` default true), `RateLimitOptions`, `ObservabilityOptions`.
- At startup log the **connection budget**: `replicas × pool ≤ max_connections − reserved`. Log a warning (not a failure) if `DB_MAX_POOL_SIZE × EXPECTED_REPLICAS` exceeds `DB_MAX_CONNECTIONS` (both env-configurable, informational). Document the formula in README.
- Move every hardcoded value flagged in `feedback.md` §4.2 into options; keep defaults identical to today so behavior is unchanged.
- Update `.env.example` and the README env table.

**Gate:** tests green; changing an env var changes behavior (add one test for lock timeout/sweeper interval parsing and validation failures such as pool size ≤ 0).

---

### WP3: Reliability & graceful shutdown (F6, health, cancellation)

1. **Health split:** `GET /health/live` (process only, no DB), `GET /health/ready` (DB reachable **and** migrations finished). Keep `GET /health` returning the readiness result for backward compatibility.
2. **Single connection per request** in `CreateHoldAsync`: run the fast-path read on the same connection used for the transaction (open once, read, then `BeginTransaction`), removing the second checkout (F6).
3. **Cancellation done correctly:**
   - Pass `HttpContext.RequestAborted` into **read endpoints** and into the **lock-wait phase** of writes (so abandoned requests stop queueing on the lock).
   - Do **not** let a client disconnect cancel `COMMIT` (use `CancellationToken.None` for the commit step); confirm/cancel are idempotent, so a retry is safe. Document this choice in the ADR (WP10).
   - Sweeper uses `stoppingToken`.
4. **Graceful shutdown:** `HostOptions.ShutdownTimeout = 25s`, compose `stop_grace_period: 30s`. Kestrel limits: `MaxRequestBodySize` ≈ 8 KB, `RequestHeadersTimeout` ≈ 10 s, `KeepAliveTimeout` sensible, `AddServerHeader = false`.
5. Test: start a slow request, send SIGTERM, assert in-flight work completes and the process exits 0 within the grace period (a script under `scripts/` is acceptable).

**Gate:** tests green; `docker compose stop backend` during a hammer run produces no corrupted state (`held + confirmed <= totalSeats`, no stuck transactions: `SELECT count(*) FROM pg_stat_activity WHERE state = 'idle in transaction'` is 0).

---

### WP4: Code architecture: SRP / DIP / ISP (feedback §2.2, §4.1)

Keep behavior identical. Refactor in small steps with tests green after each.
- **Interfaces:** `IClassService`, `IHoldService` registered in DI (endpoints depend on interfaces).
- **Data access layer:** introduce `IHoldStore` / `IClassStore` (or `IHoldRepository`) implemented with Npgsql. **All SQL strings live there** (e.g., `Data/Sql/HoldQueries.cs` constants or the store classes), none in services or endpoints.
- **Unit of work for the locked transaction:** a small `HoldTransaction` abstraction exposing the steps (`LockClassAsync`, `ExpireStaleAsync`, `FindActiveForStudentAsync`, `CountActiveAsync`, `InsertAsync`, `CommitAsync`) so `HoldService` contains only the business sequence and decisions.
- **Pure domain state machine:** `HoldStatus` transitions (`HELD→CONFIRMED|CANCELLED|EXPIRED`, terminal states) implemented as a pure, unit-tested class with no I/O. SQL status literals come from one place.
- `AppDbContext`: keep only what it is used for (schema/migrations). If it is used only as a connection factory, replace that with `NpgsqlDataSource` registered in DI, **or** leave it but document why. Do not leave an unexplained dual setup.
- **ADR** `docs/adr/0002-raw-sql-for-locking.md`: why raw SQL for the locking path, and how the schema is kept in sync (integration tests exercise every query).

**Gate:** all existing tests pass unchanged; new unit tests for the state machine and for `HoldService` using fake stores (no database) cover: full class → `NO_SEATS_AVAILABLE`, duplicate student → `ALREADY_APPLIED`, ownership → `NOT_YOUR_HOLD`, expired confirm → `HOLD_EXPIRED`, idempotent confirm.

---

### WP5: Performance: measure first, then optimize (F2, polling)

**Step 1: k6 baseline (commit the scripts under `loadtest/`).** Three scenarios:
1. **Stampede:** 500 VUs, one iteration each, all hitting one class with 120 seats. Treat `201` and `409` as expected, everything else as failure.
2. **Mixed read/write:** 1000-2000 VUs polling `GET /classes` every 4 s plus 100-200 VUs doing hold/confirm/cancel.
3. **Soak (short in CI, long manually):** steady 100 rps for 10-30 minutes; watch memory and DB connections.

```js
// loadtest/stampede.js (essentials)
import http from 'k6/http';
import { check } from 'k6';
const API = __ENV.API || 'http://localhost:8080';
export const options = {
  scenarios: { stampede: { executor: 'per-vu-iterations', vus: 500, iterations: 1, maxDuration: '60s' } },
  thresholds: { http_req_failed: ['rate<0.01'], http_req_duration: ['p(95)<2000'] },
};
export function setup() {
  const r = http.post(`${API}/classes`, JSON.stringify({ name: `k6-${Date.now()}`, totalSeats: 120 }),
    { headers: { 'Content-Type': 'application/json' } });
  return { classId: r.json('id') };
}
export default function (d) {
  const sid = '2015' + String(__VU).padStart(6, '0') + String(__ITER).padStart(7, '0'); // 17 digits
  const res = http.post(`${API}/classes/${d.classId}/holds`, null,
    { headers: { 'X-Student-Id': sid }, responseCallback: http.expectedStatuses(201, 409) });
  check(res, { 'is 201 or 409': (r) => r.status === 201 || r.status === 409 });
}
export function teardown(d) {
  const c = http.get(`${API}/classes/${d.classId}`).json();
  if (c.heldSeats + c.confirmedSeats > c.totalSeats) throw new Error('OVERSOLD');
}
```
Record results in `docs/PERFORMANCE.md` (hardware, command, date, p50/p95/p99, error rate, `503` count). This is the **baseline**.

**Step 2: optimizations: keep each only if it measurably helps and all tests stay green; otherwise revert and document the result.**
- **Reduce lock hold time (fewer round trips inside the lock).** Use `NpgsqlBatch` to pipeline the unconditional statements (`SET LOCAL lock_timeout`, `SELECT ... FOR UPDATE`, clock read, expire-stale `UPDATE`) in one round trip. Then do checks + insert as one conditional `INSERT … SELECT … WHERE (active count < total) AND NOT EXISTS (active hold for this student) RETURNING …`; if no row returns, run one diagnostic query to decide `NO_SEATS_AVAILABLE` vs `ALREADY_APPLIED`. Handle a rare unique-index violation (`23505`) by retrying once. Generate IDs before `BEGIN`.
- **Coalesce identical in-flight `GET /classes` queries** (singleton service, own connection per flight, **not** a TTL cache): concurrent callers share one running query. Rules to keep read-your-writes: maintain a local write-version counter bumped on every successful write (create class, hold, confirm, cancel, sweep); a caller may only join a flight that started **after** the latest local write. Add a test: create hold → immediately `GET /classes` → counts include it.
- **Client:** add ±20% jitter to the 4 s poll, exponential backoff (cap ~30 s) on errors, continue pausing when the tab is hidden, and never overlap requests.
- **Index review:** run `EXPLAIN (ANALYZE, BUFFERS)` on the `GET /classes` aggregate, the count query, and `GET /me/holds`; confirm index use; record plans in `docs/PERFORMANCE.md`. Add an index only if a plan proves it is needed.

**Step 3: re-run k6.** Add a before/after table to `docs/PERFORMANCE.md`. State the real bottleneck found (do not assume it is the lock).

**Gate:** tests + hammer green; before/after numbers recorded; any reverted optimization is listed with the reason.

---

### WP6: Security hardening within the contract (F1, F3, security matrix)

1. **Identity threat model (no contract change).** Keep header identity. Add `SECURITY.md` explaining: why the header is used (assignment mandate), what it does and does not protect, residual risks (impersonation if an ID is known, scraping), and the production design (OTP/verification of the birth registration number → short-lived signed token → server binds ownership to claims; migration path that keeps the header for backward compatibility during rollout).
2. **Mitigations that do not alter responses:**
   - Hold and class IDs generated with `RandomNumberGenerator` (cryptographically random, not sequential or time-based) so `GET /holds/{id}` is not enumerable.
   - `GET /holds/{id}` (no header, per spec) must **not** expose `studentId`. Only the `POST …/holds` response includes it (as in the contract). Verify `/me/holds` items only return the caller's data.
   - Ownership checks (`403 NOT_YOUR_HOLD`) verified by tests for confirm and delete.
3. **Rate limiting (`Microsoft.AspNetCore.RateLimiting`, built in):** policies for `POST /classes/{id}/holds` and `/confirm`, partitioned by `X-Student-Id` **and** client IP. Config via `RATE_LIMIT_ENABLED` (default **false**), `RATE_LIMIT_HOLDS_PER_MINUTE_PER_STUDENT`, `RATE_LIMIT_PER_MINUTE_PER_IP`. Rejections return `429` with the standard error body and a `Retry-After` header. Honor `X-Forwarded-For` only from trusted proxies (`ForwardedHeaders` with known proxies in the prod override). Tests: with the limiter **on**, the Nth+1 request is `429`; with it **off** (default), the concurrency tests pass unchanged.
4. **Web hardening:** strict CORS (explicit origins, never `*`, still allowing `X-Student-Id` and `DELETE`), Swagger only when `SWAGGER_ENABLED=true`, HSTS in Production behind TLS, no stack traces in responses, request-size limits (WP3), `docker compose` DB not published (WP1).
5. **Supply chain:** `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev` run in CI (WP9); fix or document findings; add `.github/dependabot.yml` for nuget, npm, docker, github-actions.
6. **Update the security matrix** in `docs/SECURITY.md` with each item's status: Mitigated / Accepted by spec / Not applicable, each with evidence.

**Gate:** tests green with defaults; limiter tests green with it enabled; `SECURITY.md` written.

---

### WP7: Observability (feedback §2.3, §2.4)

- **Structured JSON logs** using the built-in `AddJsonConsole` formatter (no new package). Every request log includes `traceId`, method, path, status, duration. Write path logs lock-wait duration and outcome (`created`, `no_seats`, `already_applied`).
- **Correlation:** accept/generate `X-Request-Id`, echo it in the response header, include it in logs, and add it as **`traceId`** in error bodies. **Additive only:**
```json
{ "error": { "code": "NO_SEATS_AVAILABLE", "message": "No seats are left in this class.", "traceId": "..." } }
```
  (Optionally also add RFC 7807 top-level `type/title/status/detail`; existing `error.code` and `error.message` stay.)
- **Metrics** with `System.Diagnostics.Metrics` (built in): counters `holds_created_total`, `holds_rejected_total{reason}`, `confirms_total`, `cancels_total`, `expired_swept_total`, histogram `hold_lock_wait_seconds`, `hold_transaction_seconds`.
- **Optional (one package, `OpenTelemetry.Exporter.Prometheus.AspNetCore`):** `/metrics` enabled only when `METRICS_ENABLED=true`, never exposed through the public edge. Optional compose profile `observability` with Prometheus + a provisioned Grafana dashboard. Skip this if time is short; the built-in logs and meters are the required part.

**Gate:** a test asserts `X-Request-Id` echo and `traceId` in an error body; logs are valid JSON lines (`docker compose logs backend | head | jq .` works).

---

### WP8: Testing & quality (feedback §11)

1. **Unit tests** (no database): state machine, `HoldService` decision logic with fake stores (WP4), options validation (WP2), rate-limit partitioning logic.
2. **Contract tests (high value because graders test the API):** assert for every endpoint the exact field names, `status` uppercase strings, timestamps ending in `Z`, `201/200/204/409` codes, empty-body POSTs accepted with no `Content-Type`, and `GET /holds/{id}` not leaking `studentId`. Run them against both an in-process host and the running compose stack (smoke script `scripts/contract-smoke.sh`).
3. **Extra concurrency tests:** parallel mix of hold + confirm + cancel on a nearly-full class never breaks `held + confirmed <= total`; two hosts (two `WebApplicationFactory` instances or two processes) on the same DB; **two processes starting at once** both migrate safely (advisory lock).
4. **Read-your-writes test** for the coalescing change (WP5).
5. **Frontend tests:** extend Vitest/RTL for countdown reaching zero (fake timers), poll jitter/backoff, error mapping for `409/503/network`, Student ID persistence.
6. **E2E (optional, one spec):** Playwright in a compose profile `e2e`: apply → see countdown → let it expire (`HOLD_DURATION_SECONDS=3`) → Pay button disabled **without reload**. Keep it to this single high-value scenario.
7. All tests runnable with Docker only (`docker compose --profile test run --rm ...`), documented in the README.

**Gate:** full suite green locally and via Docker commands; test inventory table added to the README.

---

### WP9: CI/CD & operations (feedback §6 of the original audit prompt)

1. **`.github/workflows/ci.yml`:** jobs
   - `backend`: restore, build, test (Postgres service or compose test profile), vulnerable-package check.
   - `frontend`: `npm ci`, lint, test, build, `npm audit --omit=dev`.
   - `images`: build both images, scan with Trivy (fail on HIGH/CRITICAL, with documented ignores if any).
   - `compose-smoke`: `docker compose up -d --wait`, run `scripts/contract-smoke.sh` and `scripts/hammer.sh`, `docker compose down` (never `-v` on a persistent environment).
   - Optional: short k6 smoke with thresholds.
2. **Ops scripts and runbook (`docs/RUNBOOK.md`):**
   - `scripts/backup.sh` (`docker compose exec -T db pg_dump -U "$POSTGRES_USER" -Fc "$POSTGRES_DB" > backups/<timestamp>.dump`) and `scripts/restore.sh` (`pg_restore --clean --if-exists`), with a documented **restore test**.
   - **Migration policy:** forward-only, expand/contract (backward-compatible changes), run under the advisory lock before serving traffic.
   - **Deploy & rollback:** images tagged with the git SHA; deploy = pull new tag, `up -d` gated on health; rollback = redeploy previous tag (schema is backward-compatible by policy) or restore the last backup for data problems. State plainly that **true zero-downtime needs ≥ 2 backend replicas behind the edge proxy** (Architecture A) and that a single-host compose is not highly available.
   - Incident basics: where to look (logs, `/health/ready`, `pg_stat_activity`, lock waits), and how to kill a stuck transaction safely.
3. `Makefile` or `scripts/README` with the common commands (`up`, `test`, `hammer`, `loadtest`, `backup`).

**Gate:** CI workflow passes on a clean push (or is validated with `act`/syntax check if CI cannot run); backup then restore into a scratch database succeeds and row counts match.

---

### WP10: Documentation that shows engineering judgment

Create or update:
- `docs/ARCHITECTURE.md` with Mermaid diagrams:
  - **Current (as implemented)**: browser → nginx (static) → backend instance(s) → PostgreSQL; sweeper; locking sequence diagram (hold creation).
  - **Architecture A (implemented via `docker-compose.prod.yml`)**: edge nginx → N stateless backend replicas → PostgreSQL, health-gated.
  - **Architecture B (future, NOT implemented)**: CDN/WAF, queue/waiting-room for national-scale spikes, read replica for `GET /classes`, PgBouncer (with the migrator connecting directly), Redis only as a *non-authoritative* admission pre-filter, SSE for live seats. For each: problem solved, why the current design is enough today, operational cost, and the trigger metric that would justify adding it (e.g., "p95 hold latency > X ms in k6 at the target load").
- `docs/adr/` (short ADRs): `0001-db-row-lock-over-redis.md`, `0002-raw-sql-for-locking.md`, `0003-polling-with-coalescing-over-sse.md`, `0004-header-identity-per-spec.md`, `0005-cancellation-and-commit-semantics.md`.
- `docs/SECURITY.md` (WP6), `docs/PERFORMANCE.md` (WP5), `docs/RUNBOOK.md` (WP9), `docs/FEEDBACK_RESPONSE.md` (all findings with decision + commit hash).
- `README.md`: add "Production deployment (Architecture A)", "Testing (unit/contract/concurrency/load)", "Known limitations", and the connection-budget formula.
- `ASSUMPTIONS.md`: add the new decisions (rate limit default off and why, cancellation semantics, coalescing rules).
- `AI_USAGE.md`: add real mistakes made during this hardening pass (only genuine ones). The human owner rewrites it in their own words.

**Gate:** every doc link resolves; Mermaid renders; `FEEDBACK_RESPONSE.md` has no empty rows.

---

## 3. Do NOT Implement (document instead)

| Item | Reason |
|------|--------|
| JWT/OTP/session auth | Breaks the mandated header contract and automated tests; documented design instead (WP6). |
| Redis as seat counter/gatekeeper | A second source of truth risks overselling or ghost seats; contradicts the DB-as-lock requirement and the two-process bonus. |
| TTL caching of `GET /classes` | Breaks read-your-writes. Use coalescing. |
| Kubernetes, service mesh, read replicas, CDN | Disproportionate to the system; documented as Architecture B with trigger metrics. |
| Event bus / outbox / microservices | No downstream consumers in scope. |
| Rewriting the data layer to EF Core LINQ | Risks locking semantics; the fix is separation of concerns, not a different ORM. |
| PgBouncer in the default stack | Transaction mode conflicts with session advisory locks; optional profile at most. |
| Rate limiting ON by default | Would reject graders' parallel requests with `429`. |

---

## 4. Commit Plan (one logical change each)

1. `docs: baseline results and feedback triage table (F*)`
2. `build(docker): restore-layer caching, non-root backend, pinned tags (F4)`
3. `build(docker): unprivileged nginx, runtime config in /tmp, cache and security headers`
4. `build(compose): healthchecks, limits, restart, log rotation, hardening; db not published`
5. `build(compose): prod override with edge proxy, required secrets, scale support`
6. `refactor(config): typed validated options, connection budget log`
7. `feat(health): live/ready split; single connection in fast path (F6)`
8. `feat(reliability): cancellation semantics, graceful shutdown, Kestrel limits`
9. `refactor(data): interfaces, SQL isolated in stores, domain state machine`
10. `test(unit): state machine and service decisions with fakes`
11. `perf(loadtest): k6 scenarios and baseline results`
12. `perf(hold): pipelined lock phase` *(only if measured improvement)*
13. `perf(classes): coalesce in-flight list queries; client jitter and backoff` *(only if measured improvement)*
14. `feat(security): secure random ids, no studentId leak, configurable rate limiting`
15. `feat(observability): JSON logs, request id, traceId in errors, metrics`
16. `test(contract): exact-shape contract tests and smoke script`
17. `test(concurrency): mixed ops, two-host, simultaneous migration, read-your-writes`
18. `ci: workflow, dependabot, trivy, compose smoke`
19. `ops: backup/restore scripts, runbook, migration and rollback policy`
20. `docs: architecture A/B diagrams, ADRs, security, performance, feedback response`

---

## 5. Final Verification Gate (everything must be true before you say "done")

**Regression safety**
- [ ] Section 6 contract unchanged; contract tests green on in-process and compose stacks.
- [ ] Backend, frontend, and concurrency tests green; `scripts/hammer.sh` green (two processes, one DB).
- [ ] Clean clone + only Docker + no `.env`: `docker compose up --build` works; `down` → `up` keeps data.

**Hardening**
- [ ] Both app containers run as non-root; `read_only` + `cap_drop: ALL` + `no-new-privileges` where verified working.
- [ ] Healthchecks pass; `/health/live`, `/health/ready`, `/health` all behave as documented.
- [ ] Graceful shutdown verified (no stuck transactions, no oversell).
- [ ] Prod override works with `--scale backend=2` behind the edge proxy; DB and backend ports not published.
- [ ] No hardcoded operational constants remain (options + env + `.env.example`).

**Quality & evidence**
- [ ] SQL lives only in the data layer; services and endpoints depend on interfaces; state machine unit-tested.
- [ ] k6 scripts committed; `docs/PERFORMANCE.md` has real before/after numbers (no estimates).
- [ ] CI workflow, Dependabot, Trivy, vulnerability checks in place.
- [ ] Backup/restore tested; runbook, migration and rollback policy written.

**Judgment artifacts**
- [ ] `docs/FEEDBACK_RESPONSE.md` complete (finding → verdict → decision → commit).
- [ ] `ARCHITECTURE.md` (Mermaid current/A/B), 5 ADRs, `SECURITY.md` written; "Do NOT implement" items documented with reasons.
- [ ] `AI_USAGE.md` contains only real incidents.

---

## 6. What Each Area Can Honestly Reach

| Area (from the `feedback.md` scorecard) | Where this prompt moves it | Ceiling and why |
|---|---|---|
| Concurrency & correctness | Keep intact; add more proof (mixed-op and two-host tests) | Already strong; do not risk it |
| Code architecture | Interfaces, isolated SQL, pure state machine, ADR | Strong without an ORM rewrite |
| Scalability | Measured baseline, shorter lock phase, coalesced reads, Architecture A working, B documented | Evidence-based; no unmeasured claims |
| DevOps & containers | Non-root, healthchecks, limits, prod override, CI, scans, backup/restore | Strong |
| Observability | JSON logs, request IDs, metrics | Strong; full tracing stack stays optional |
| Testing | Unit + contract + concurrency + load + one E2E | Strong |
| Security | Threat model, secure IDs, rate limiting, headers, supply-chain checks | **Capped by the spec:** header identity is mandated; the credible position is a documented design, not a contract-breaking change |

---

## 7. Pitfalls

- Implementing JWT, Redis counters, or default-on rate limits "because the audit said so" and silently failing the graders' API tests.
- Quoting estimated numbers from `feedback.md` as facts.
- Adding PgBouncer in transaction mode while the migrator still uses session advisory locks.
- A TTL cache or coalescing that joins a query started before the caller's own write (stale counts).
- Letting a client disconnect cancel the commit step.
- `read_only` containers without `tmpfs` for `/tmp`, nginx cache, and run dirs (container fails to start).
- nginx-unprivileged still mapped to port 80 in compose (now 8080 inside).
- Making `docker compose up` require a `.env` or secrets (breaks the one-command requirement).
- Healthcheck using `curl` in an image that does not have it.
- Treating `409` as a failure in k6 thresholds (it is an expected outcome in a stampede).
- Refactoring and changing behavior in the same commit.
- Running `docker compose down -v` anywhere except a disposable CI environment.
