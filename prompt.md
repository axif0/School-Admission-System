# PROMPT: Build the "School Admission System" (Seat-Hold Take-Home Assignment)

> **Version 3** (re-verified line by line against the original assignment).
> Put this file at the repo root and tell your AI coding tool:
> *"Read prompt.md fully. Follow the phases in Section 14 in order. After each phase, run the verification gate, report results, and wait for my go-ahead."*

---

## 0. Operating Instructions (read first)

**Your role:** Senior full-stack engineer (C# / .NET 8, PostgreSQL, React). Deliver a complete, runnable, tested system that stays correct under heavy concurrency.

**Source labels used in this file:**
- **[SPEC]** = stated directly by the assignment. Do not change.
- **[CHOICE]** = the assignment is silent; this is our decision. It may be changed only if you record the reason in `ASSUMPTIONS.md`.

**Priority order when goals conflict:**
1. API contract exactness (Section 6)
2. Concurrency correctness / no overselling (Sections 5, 8)
3. Required UI behaviors (Section 9)
4. Docker "one command" delivery (Section 11)
5. Tests (Section 10)
6. Visual polish (lowest; the UI only needs to work and be easy to use)

**Working rules:**
- **Do not ask me questions.** The assignment is deliberately vague **[SPEC]**. Where it is silent, use the decisions in **Section 7**. For anything not covered there, choose the most reasonable option and **record it with a reason in `ASSUMPTIONS.md`**.
- **Never remove or rename** any path, header, field, or status code listed in Section 6. You **may add** extra response fields **[SPEC]**.
- **Commit small and often** (Section 13). No giant final commit **[SPEC]**.
- **Verification gate after every phase:** build passes, relevant tests pass, and you state the exact commands you ran and their results. **Do not start the next phase while anything is failing.**
- **Keep a private mistake log** (scratch note, not committed): each time you got something wrong and a test/review caught it, write one line. It is raw material for `AI_USAGE.md`. Never invent entries.
- Code, comments, commit messages, and docs are in **English**. UI text is **English by default**, stored in one file (e.g., `src/i18n/messages.ts`) so it can be translated. A Bengali translation with a language toggle is an **optional nice-to-have** (do it last, only if everything else is done). **[CHOICE]** The assignment only says errors must be shown "in language the user understands", meaning plain, non-technical wording.
- Never hardcode secrets, connection strings, or URLs. Use environment variables.

---

## 1. Non-Negotiables (TL;DR)

1. `held + confirmed` **never exceeds** `totalSeats`, even with thousands of simultaneous requests **and with two backend processes sharing one database**. The **database** enforces this (row lock), never in-memory locks. **[SPEC]**
2. Expired holds free their seat **automatically**, with no API call needed. **[SPEC]**
3. A student has **at most one active/confirmed application per class**. **[SPEC]**
4. Only the owner's non-expired `HELD` hold can be confirmed. **Confirm is idempotent** (`confirmedSeats` rises once). **[SPEC]**
5. Data survives `docker compose down` → `docker compose up`. **[SPEC]**
6. `docker compose up` (only Docker installed) starts **PostgreSQL + backend + frontend**. **[SPEC]**
7. The UI never shows a blank page; every error is in plain language; countdown and seat counts update **without page reload**. **[SPEC]**
8. The Section 6 API contract is followed **exactly**, because automated tests will call the API directly. **[SPEC]**

---

## 2. Scope

**In scope:** classes, seat holds, expiry, confirm (simulated payment), cancel, student's own applications list, React UI, Docker, tests, docs.

**Out of scope (do NOT build):** login/authentication, real payment gateway **[SPEC]**, email/SMS, waiting list/queue, lottery **[SPEC: no lottery]**, admin login, user accounts, file uploads, pagination.

---

## 3. Problem Statement **[SPEC]**

A private school takes admissions **online** for the new academic year.

- **First-come-first-served** ("whoever applies first gets in"). No lottery.
- Every **class** has limited seats **per shift** (e.g., "Class 6 - Morning Shift" has 120 seats). Each shift is created as its **own separate class** with its own seat count (so "Class 6 - Morning Shift" and "Class 6 - Day Shift" are two independent classes).
- When the window opens, **thousands of guardians apply at once.**
- Applying **holds** a seat in the student's name for a limited time.
- If the **admission fee is paid within that time**, admission is **confirmed**.
- Otherwise the seat is **automatically released** for others.
- School admins and guardians do everything **from the UI**.

---

## 4. Tech Stack & Environment

### 4.1 Backend
- **C# on .NET 8 or newer** **[SPEC]**, ASP.NET Core Web API (controllers or minimal APIs).
- **PostgreSQL** **[SPEC]**. Use version **16** **[CHOICE]**. ORM is free choice **[SPEC]** (recommended: EF Core + Npgsql, with raw parameterized SQL for the locking queries).
- Listens on **`http://localhost:8080`** **[SPEC]** (in Docker: `ASPNETCORE_URLS=http://+:8080`).
- All requests and responses are JSON **[SPEC]**.

### 4.2 Frontend
- **React 18 or newer**, JavaScript or TypeScript **[SPEC]**. **TypeScript + Vite** **[CHOICE; Vite is recommended by the spec]**. UI library is free choice **[SPEC]**.
- Served in Docker on host port **3000** **[CHOICE]**.

### 4.3 Environment variables (document all in README and `.env.example`)

| Variable | Used by | Default | Purpose |
|----------|---------|---------|---------|
| `ConnectionStrings__Default` | backend | set in compose | PostgreSQL connection string (**must be env-configurable [SPEC]**) |
| `HOLD_DURATION_SECONDS` | backend | `600` **[SPEC]** | How long a hold lasts |
| `CORS_ALLOWED_ORIGINS` | backend | `http://localhost:3000` | Comma-separated allowed origins |
| `ASPNETCORE_URLS` | backend | `http://+:8080` | Listen address |
| `API_URL` | frontend container | `http://localhost:8080` | Backend URL used by the browser (**must be env-configurable [SPEC]**, see 11.2) |
| `TEST_ADMIN_CONNECTION_STRING` | test runner | set in compose | Used by tests to create/drop temporary databases |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | db | set in compose | Database credentials |

---

## 5. Domain Model & Business Rules

### 5.1 Entities
- **Class:** `id`, `name`, `totalSeats`.
- **Hold (application):** `id`, `classId`, `studentId`, `status`, `expiresAt`, plus timestamps.

### 5.2 Student identity (no login) **[SPEC]**
- A single input **at the top of the page** takes the student's **17-digit birth registration number** (example `20152691234567890`). The UI uses it as the student's identity.
- Sent as the **`X-Student-Id`** header on student-specific requests.
- **UI** validation: exactly 17 digits, digits only. Persist the value in `localStorage` so refresh/reopen keeps it.
- **Backend** validation **[CHOICE, deliberately lenient]:** the header must be present, non-blank after trimming, at most 64 chars, with no control characters; otherwise `400 INVALID_STUDENT_ID`. The backend does **not** reject other formats, because the assignment says the value *will be* 17 digits (an input guarantee), and automated API tests might use other ID strings. Record this in `ASSUMPTIONS.md`.

### 5.3 Hold lifecycle
```
           POST /classes/{id}/holds
                     |
                     v
                  [ HELD ] --- confirm (fee paid in time) ---> [ CONFIRMED ]
                   |   \
   time runs out   |    \--- DELETE (student cancels) ---> [ CANCELLED ]
                   v
               [ EXPIRED ]
```
Exact status strings **[SPEC]**: `HELD`, `CONFIRMED`, `EXPIRED`, `CANCELLED`. Only `HELD` can become `EXPIRED`. `CONFIRMED` and `CANCELLED` never change again.

### 5.4 Seat counting (per class)
- `confirmedSeats` = holds with status `CONFIRMED`.
- `heldSeats` = holds with status `HELD` **and** `expires_at > now` (expired-but-unswept rows do **not** count).
- `availableSeats` = `totalSeats - heldSeats - confirmedSeats`.

### 5.5 Mandatory rules **[SPEC]**

| # | Rule |
|---|------|
| B1 | **Never oversell:** `held + confirmed <= totalSeats` always, under any concurrency. |
| B2 | Hold duration = env `HOLD_DURATION_SECONDS` (**default 600**). |
| B3 | Expiry is automatic: the seat becomes available again with no API call. |
| B4 | At most **one active (`HELD`, not expired) or `CONFIRMED`** application per student **per class**. Re-applying after expiry/cancel is allowed **[CHOICE]**. |
| B5 | Only the student's **own, non-expired `HELD`** hold can be confirmed. |
| B6 | Confirm is **idempotent:** repeated confirms succeed and `confirmedSeats` rises **once**. |
| B7 | **No data loss on restart:** use a **named Postgres volume**. |

---

## 6. API Contract (MUST match exactly) **[SPEC]**

Base URL `http://localhost:8080`. JSON. Student identity via **`X-Student-Id`** header. IDs like `c_123` / `h_789` are examples only; **ID format is free choice**.

| # | Method & Path | Purpose | Header |
|---|---------------|---------|--------|
| 1 | `POST /classes` | Create class | none |
| 2 | `GET /classes` | List classes with seat counts | none |
| 3 | `GET /classes/{classId}` | One class's seat status | none |
| 4 | `POST /classes/{classId}/holds` | Apply (hold a seat) | `X-Student-Id` |
| 5 | `GET /holds/{holdId}` | Current state of an application | none |
| 6 | `POST /holds/{holdId}/confirm` | Pay fee, confirm admission | `X-Student-Id` |
| 7 | `DELETE /holds/{holdId}` | Cancel application | `X-Student-Id` |
| 8 | `GET /me/holds` | All of this student's applications | `X-Student-Id` |

**Success/failure codes stated by the assignment:** `201` for #1 and #4, `200` for #2, #3, #5, #6, #8, `204` for #7, and **`409` when no seat is available** on #4. All other error codes are our choice (Section 7).

**1. `POST /classes`**
```jsonc
// request
{ "name": "Class 6 - Morning Shift", "totalSeats": 120 }
// 201 response
{ "id": "c_123", "name": "Class 6 - Morning Shift", "totalSeats": 120 }
```

**2. `GET /classes`** and **3. `GET /classes/{classId}`** (same item shape; #2 returns an array)
```jsonc
// 200 response (#2 shown)
[
  { "id": "c_123", "name": "Class 6 - Morning Shift", "totalSeats": 120,
    "heldSeats": 15, "confirmedSeats": 80, "availableSeats": 25 }
]
```

**4. `POST /classes/{classId}/holds`**
```jsonc
// 201 response
{ "holdId": "h_789", "classId": "c_123", "studentId": "20152691234567890",
  "status": "HELD", "expiresAt": "2026-11-01T04:10:00Z" }
```
No seats → **`409`**.

**5. `GET /holds/{holdId}`**
```jsonc
// 200 response
{ "holdId": "h_789", "classId": "c_123", "status": "HELD", "expiresAt": "2026-11-01T04:10:00Z" }
```
`status` ∈ `HELD | CONFIRMED | EXPIRED | CANCELLED`. If a `HELD` hold's `expiresAt` has passed, this **must return `EXPIRED`** even if no background job has updated the row.

**6. `POST /holds/{holdId}/confirm`**
```jsonc
// 200 response
{ "holdId": "h_789", "status": "CONFIRMED" }
```
The UI's "Pay Admission Fee" button counts as payment success. **No real gateway.**

**7. `DELETE /holds/{holdId}`** → **`204`**, no body.

**8. `GET /me/holds`**
```jsonc
// 200 response
[ { "holdId": "h_789", "classId": "c_123", "className": "Class 6 - Morning Shift",
    "status": "HELD", "expiresAt": "2026-11-01T04:10:00Z" } ]
```
Statuses must be the **derived** status (elapsed `HELD` → `EXPIRED`). Order: newest first **[CHOICE]**.

**Serialization rules (so automated tests parse correctly) [CHOICE]:**
- JSON property names exactly as shown (camelCase).
- `status` is always the **uppercase string** (if you use a C# enum, add `JsonStringEnumConverter` and make sure the output is `HELD`, not `Held` or `0`).
- Timestamps are **ISO-8601 UTC ending in `Z`**.
- Endpoints #4, #6, #7 must accept an **empty body** (and `{}`) and must **not** require a `Content-Type` header (no `415`).

**Allowed additions:** `serverTime` (ISO-8601 UTC, taken from the **database clock**) on hold responses, `createdAt`, and `studentId` on #5 and #8 items. Also add `GET /health`.

---

## 7. Pre-Made Decisions (use these; do not re-debate; copy them into `ASSUMPTIONS.md` with a one-line reason)

**Source:** SPEC = the assignment says so; CHOICE = the assignment is silent.

| Topic | Decision | Source |
|-------|----------|--------|
| Create class success | `201` | SPEC |
| No seats on apply | `409 NO_SEATS_AVAILABLE` | SPEC (409) |
| Cancel success | `204` | SPEC |
| ID format | `c_` + 12-char unique id (classes), `h_` + 12-char unique id (holds), e.g., base32 of a GUID/ULID | CHOICE (spec allows) |
| Error body (everywhere) | `{ "error": { "code": "NO_SEATS_AVAILABLE", "message": "No seats are left in this class." } }`, human-readable, no stack traces | CHOICE |
| Check order for hold actions | 1) header `400` → 2) exists `404` → 3) ownership `403` → 4) state/business rule `409` | CHOICE |
| Student already applied **and** class is full | `ALREADY_APPLIED` (student check precedes seat check) | CHOICE |
| Missing/blank/invalid `X-Student-Id` | `400 INVALID_STUDENT_ID` (lenient format, see 5.2) | CHOICE |
| Invalid class body | `400 VALIDATION_ERROR`. `name`: trimmed, 1-100 chars. `totalSeats`: integer, 1 to 100000 | CHOICE |
| Class/hold not found (also malformed IDs) | `404 NOT_FOUND` | CHOICE |
| Student already has active/confirmed hold in the class | `409 ALREADY_APPLIED` | CHOICE (409) |
| Confirm on expired / cancelled hold | `409 HOLD_EXPIRED` / `409 HOLD_CANCELLED` | CHOICE |
| Confirm/Delete someone else's hold | `403 NOT_YOUR_HOLD` | CHOICE |
| Confirm an already `CONFIRMED` hold (same owner) | `200` `CONFIRMED`, no change | SPEC (idempotent) |
| `DELETE` on `HELD` | `204`, becomes `CANCELLED`, seat freed | SPEC (204) |
| `DELETE` on `CONFIRMED` | `409 ALREADY_CONFIRMED` (refunds out of scope) | CHOICE |
| `DELETE` on `CANCELLED` or `EXPIRED` | `204` (idempotent no-op) | CHOICE |
| Same student, different classes | Allowed (B4 is per class). Note: a student could therefore hold both shifts of "Class 6". Mention in `ASSUMPTIONS.md`. | SPEC wording ("in a class") |
| Duplicate class names | Allowed; the ID is the identity | CHOICE |
| `expiresAt` after confirm | Keep original value; UI ignores it once `CONFIRMED` | CHOICE |
| `GET /holds/{id}` | Needs no header (matches spec table); unguessable IDs | SPEC (no header listed) |
| `GET /classes` ordering | By creation time ascending; no pagination | CHOICE |
| Time source | **Database clock** for all expiry decisions (8.3) | CHOICE |
| Lock/overload failure | Lock timeout or pool exhaustion → `503 TEMPORARILY_BUSY` with a friendly message | CHOICE |
| UI polling interval | Class list every **4 seconds** ("every few seconds") | SPEC (few seconds), CHOICE (4 s) |

---

## 8. Backend Design & Concurrency

### 8.1 Suggested structure
```
backend/
  src/SchoolAdmission.Api/
    Program.cs
    Endpoints|Controllers/
    Contracts/        // DTOs matching Section 6 exactly
    Domain/           // Class, Hold, HoldStatus
    Data/             // DbContext, configs, migrations
    Services/         // ClassService, HoldService, IdGenerator
    Middleware/       // global exception handling, student-id validation
    Background/       // HoldExpirySweeper (optional)
  tests/SchoolAdmission.Tests/
  Dockerfile
```
Keep controllers/endpoints thin; business logic lives in services.

### 8.2 Database schema
- `classes(id text PK, name text NOT NULL, total_seats int NOT NULL CHECK (total_seats > 0), created_at timestamptz NOT NULL)`
- `holds(id text PK, class_id text NOT NULL REFERENCES classes(id), student_id varchar(64) NOT NULL, status text NOT NULL CHECK (status IN ('HELD','CONFIRMED','EXPIRED','CANCELLED')), expires_at timestamptz NOT NULL, created_at timestamptz NOT NULL, confirmed_at timestamptz NULL, cancelled_at timestamptz NULL)`
  (Use `varchar(64)`, **not** `char(17)`: fixed-width `char` pads values and would conflict with the lenient backend validation.)
- Indexes: `(class_id, status, expires_at)` and `(student_id)`.
- **Partial unique index (safety net for B4):** `UNIQUE (class_id, student_id) WHERE status IN ('HELD','CONFIRMED')`. A lazily-expired `HELD` row would still occupy this index, so the hold-creation transaction **first flips expired `HELD` rows of that class to `EXPIRED`** (step 4 below).
- **Migrations run automatically at backend startup** and must be **safe when two backend processes start together**: wrap migration in a Postgres **advisory lock** (`pg_advisory_lock`), and retry/backoff until Postgres accepts connections.

### 8.3 Concurrency strategy (REQUIRED)

**Forbidden as the correctness mechanism:** `lock`, `SemaphoreSlim`, in-memory counters, `ConcurrentDictionary`. They break with two processes. **The database is the lock.** **[SPEC bonus: two processes, one DB]**

**Clock rule:** use the **database clock**. Inside a write transaction, read `clock_timestamp()` **after acquiring the row lock** and reuse that single value for every comparison and for `expires_at` in that transaction. (Do **not** use `now()` for writes: it returns the *transaction start* time, which can be stale after waiting on a lock.) Single-statement reads may use `now()`. `serverTime` in responses also comes from the DB clock so it is consistent with `expiresAt`.

**`POST /classes/{classId}/holds`** (one short transaction; `READ COMMITTED` is sufficient because of the lock). Generate the hold ID **before** `BEGIN` to keep the transaction short.
1. `BEGIN`.
2. `SELECT total_seats FROM classes WHERE id = @classId FOR UPDATE;` → not found = `404`. This serializes all state changes for the class across all processes.
3. `@now := clock_timestamp()`.
4. `UPDATE holds SET status='EXPIRED' WHERE class_id=@classId AND status='HELD' AND expires_at <= @now;`
5. If this student already has a `HELD`/`CONFIRMED` hold in this class → `409 ALREADY_APPLIED`.
6. Count `HELD` + `CONFIRMED` in this class. If `>= total_seats` → `409 NO_SEATS_AVAILABLE`.
7. `INSERT` the hold with `expires_at = @now + HOLD_DURATION_SECONDS`, `RETURNING` the stored row (so the response uses stored values).
8. `COMMIT`.

**Optional fast path (recommended for the "thousands at once" burst):** before `BEGIN`, run an unlocked read of the class's current non-expired `held + confirmed`; if it is already `>= total_seats`, return `409 NO_SEATS_AVAILABLE` immediately without taking the lock. This can only produce a harmless momentary "full" answer; it can never cause overselling, because every *successful* path still goes through the locked transaction. It prevents hundreds of doomed requests from queueing on one lock.

**`POST /holds/{holdId}/confirm`:** read the hold's `class_id` → lock that **class row** `FOR UPDATE` → re-read the hold inside the lock → `@now := clock_timestamp()` → apply the Section 7 check order. If `HELD`, `expires_at > @now`, and owner matches → set `CONFIRMED`, `confirmed_at`. If already `CONFIRMED` by the owner → return `200`, change nothing.

**`DELETE /holds/{holdId}`:** same locking pattern; apply the Section 7 rules.

**Reads (`GET /classes`, `GET /classes/{id}`):** one grouped SQL query (no N+1), `heldSeats` counted as `status='HELD' AND expires_at > now()`. Reads take no locks.

**Connections & overload (important):**
- Npgsql `Maximum Pool Size=50` per backend process.
- In compose, start Postgres with `-c max_connections=200` (default is 100; two backend processes with large pools would otherwise hit "too many clients").
- `SET lock_timeout = '15s'` for write transactions, and a pool/command timeout of similar size, so a burst queues instead of failing. Map lock-timeout or pool exhaustion to `503 TEMPORARILY_BUSY`.
- Always dispose transactions (`await using`) and roll back on exceptions.

### 8.4 Automatic expiry (B3)
- **Correctness comes from derived expiry:** every count uses `expires_at > now`; `GET /holds/{id}` and `GET /me/holds` map an elapsed `HELD` to `EXPIRED`.
- **Optional** `BackgroundService` sweeper (every ~5 s) persists `EXPIRED` for tidiness; it must be safe if two processes run it simultaneously (`UPDATE ... WHERE status='HELD' AND expires_at <= clock_timestamp()` is safe). **Correctness must never depend on it.**

### 8.5 Other backend requirements
- **CORS** from `CORS_ALLOWED_ORIGINS`, allowing **any header and any method** (the custom `X-Student-Id` header and `DELETE` trigger browser preflight requests; if blocked, the UI breaks even though the API works).
- Centralized validation and a **global exception middleware** returning the Section 7 error shape (unhandled → generic `500`).
- All SQL parameterized. Structured logging, no secrets in logs.
- `GET /health` checks DB connectivity.
- Swagger in Development (optional).

---

## 9. Frontend Requirements

React + Vite + TypeScript. Correct and usable beats pretty **[SPEC]**.

### 9.1 Global
- **Student ID input at the top of every page [SPEC]**, with inline validation (17 digits) and `localStorage` persistence. Changing it clears any held-application view and re-fetches that student's data.
- Routes **[CHOICE]**: `/` (Classes), `/classes/new` (Create Class), `/my-applications`, `/holds/:holdId` (Application). Refreshing any route must work (nginx SPA fallback `try_files $uri /index.html`).
- **One central API client** that reads the base URL from runtime config (11.2), attaches `X-Student-Id`, parses the standard error shape, and throws typed errors.
- A top-level **Error Boundary** so a render crash shows a friendly message, never a blank page.

### 9.2 Screens **[SPEC: four screens]**

| Screen | Behavior |
|--------|----------|
| **Create Class** (school admin) | Class name + seat count → `POST /classes`. Client validation mirroring Section 7. Success message; class appears in the list. |
| **Class List** | Per class: **total, held, confirmed, available** seats and an **"Apply"** button. "Apply" is disabled when `availableSeats === 0`, when Student ID is invalid, or when this student already has an active/confirmed application in that class (show a "Your application" link instead) **[CHOICE]**. |
| **Application** | Status, **time remaining until expiry**, **"Pay Admission Fee"** and **"Cancel"** buttons. After Apply succeeds, navigate here. |
| **My Applications** | From `GET /me/holds`: all of this student's applications with class name, status, remaining time for `HELD`, and a link to each. |

### 9.3 Mandatory UI behaviors **[SPEC]**
1. Everything works from the UI. No Postman, no direct DB access.
2. **When time runs out the UI shows `EXPIRED` and disables "Pay Admission Fee" without a reload.** Drive a 1-second tick from `expiresAt`, adjusted by the clock offset computed at fetch time (`offset = serverTime - clientNow`; `remaining = expiresAt - (clientNow + offset)`). At zero set local state to `EXPIRED`, disable Pay, and re-fetch once to reconcile with the server.
3. **Class seat numbers refresh every 4 s without reload.** Pause polling while the tab is hidden, never stack overlapping requests, clear intervals on unmount. If polling fails, show a small non-blocking "Connection problem, retrying..." banner and keep the last known data.
4. **Refresh or close/reopen the browser:** the student still sees their current applications (Student ID from `localStorage` + `GET /me/holds`; an active `HELD` hold shows the correct remaining time).
5. **Every error appears in plain human language**: at minimum no seats, already applied, hold expired, invalid input, not found, temporarily busy, and **server down / network failure**. Map `error.code` → friendly message (from `messages.ts`), fall back to the server `message`, then to "Something went wrong. Please try again." **No blank page and no console-only errors.**
6. Buttons show a loading state and are disabled while a request is in flight (no double submit) **[CHOICE]**.
7. After apply/confirm/cancel, refresh both the application view and the class list so counts are immediately correct **[CHOICE]**.
8. After `CONFIRMED`, hide the countdown and show a clear "Admission confirmed" state **[CHOICE]**.

---

## 10. Testing

### 10.1 Backend (MANDATORY: at least 3 automated tests; one MUST be a concurrency test) **[SPEC]**
Use **xUnit + `WebApplicationFactory`** against **real PostgreSQL** (not the EF in-memory provider, which cannot validate locking). Each test run creates its **own temporary database** (e.g., `school_admission_test_<guid>`, via `TEST_ADMIN_CONNECTION_STRING`) and drops it afterward. Since the app uses the **DB clock**, expiry tests set `HOLD_DURATION_SECONDS=1` through test configuration and wait with `Task.Delay`; no fake clock.

Required tests:
1. **No overselling (REQUIRED):** class with 10 seats; fire **100-200 parallel** hold requests with **distinct** student IDs → exactly **10 × 201**, the rest **409**; DB `held + confirmed == 10`; `availableSeats == 0`.
2. **Same student in parallel:** ~20 simultaneous holds on one class by one student → exactly **one 201**, the rest `409 ALREADY_APPLIED`.
3. **Expiry frees the seat:** take the last seat with a 1-second hold; after it elapses, `GET /holds/{id}` → `EXPIRED`, `availableSeats` restored, another student can hold.
4. **Confirm rules:** own valid hold → `CONFIRMED`; confirm twice → both `200`, `confirmedSeats` rose once; expired hold → `409`; other student's hold → `403`.
5. **Cancel rules:** `DELETE` → `204`, seat freed; second `DELETE` → `204`; `DELETE` on confirmed → `409`.
6. **Validation:** missing/blank `X-Student-Id`, bad class body, unknown IDs; empty body accepted on `POST .../holds`.
7. *(Nice)* Concurrent mix of hold + confirm + cancel on a nearly-full class never breaks the invariant.

### 10.2 Frontend (BONUS) **[SPEC]**
**Vitest + React Testing Library** (+ MSW to mock the API). At least: Student ID validation; class list shows counts and disables "Apply" at 0 seats; a `409` renders its friendly message; countdown reaching zero disables the Pay button (fake timers).

### 10.3 Bonus: two backend processes, one database **[SPEC]**
Provide a compose **profile** `multi` adding `backend2` on port **8081** (same DB). Provide a script (e.g., `scripts/hammer.sh` or a test) that fires parallel hold requests at **both ports simultaneously** and verifies `held + confirmed <= totalSeats`. **Explain in `ASSUMPTIONS.md`** exactly why it is safe (the class-row `FOR UPDATE` lock lives in the shared database, so it serializes across processes).

### 10.4 Tests must be runnable with Docker only **[CHOICE; follows the "only Docker installed" spec]**
Provide, e.g.:
- `docker compose --profile test run --rm backend-tests` (Dockerfile `test` stage running `dotnet test` against the compose `db` service)
- `docker compose --profile test run --rm frontend-tests` (bonus)

Document the exact commands in the README, plus the native `dotnet test` / `npm test` alternatives.

---

## 11. Docker & Delivery

### 11.1 Files **[SPEC: Dockerfiles + root compose]**
- `backend/Dockerfile`: multi-stage (SDK build → ASP.NET runtime, plus a `test` stage), exposes 8080, non-root user if easy.
- `frontend/Dockerfile`: multi-stage (Node build → nginx static server, plus a `test` stage).
- **`docker-compose.yml` at the repo root:**
  - `db`: `postgres:16`, command `postgres -c max_connections=200`, **named volume**, `healthcheck` with `pg_isready`.
  - `backend`: `depends_on: db (condition: service_healthy)`, env from 4.3, port `8080:8080`.
  - `frontend`: port `3000:80`, env `API_URL`.
  - `backend2` (profile `multi`, port `8081:8080`); `backend-tests`, `frontend-tests` (profile `test`).
- `.dockerignore` for backend and frontend, root `.gitignore`, and `.env.example`.

### 11.2 Frontend API URL as a true environment variable **[SPEC]**
Vite variables are normally fixed at build time. To change the URL **without rebuilding**: the nginx container's entrypoint writes `/usr/share/nginx/html/config.js` from the `API_URL` env var at startup (via `envsubst`), e.g. `window.__APP_CONFIG__ = { API_URL: "..." };`. `index.html` loads `/config.js` first (served `no-cache`). The app reads `window.__APP_CONFIG__.API_URL`, then falls back to `import.meta.env.VITE_API_URL`, then `http://localhost:8080`.

### 11.3 Required behavior **[SPEC]**
- From a clean clone: `docker compose up --build` → UI at **`http://localhost:3000`**, API at `http://localhost:8080`.
- Backend waits for DB, migrates, then serves.
- `docker compose down` then `up` keeps data. **Never document `down -v`** (it deletes the volume).

---

## 12. Documentation Deliverables **[SPEC]**

### 12.1 `README.md`
Short description + architecture diagram; **steps to run with Docker** (exact commands); **which URL opens the frontend**; ports; env var table; **how to run tests** (Docker commands first, native second); how to run the multi-process demo (bonus); API quick reference; short note on the concurrency design.

### 12.2 `ASSUMPTIONS.md` (short and concrete)
Copy every **[CHOICE]** row of **Section 7** with a one-line *why*, then add anything else you decided. Also cover: lenient backend `X-Student-Id` validation; time source and expiry strategy; the concurrency design and **why it is safe across multiple processes** (bonus); confirm = simulated payment; polling interval and clock-skew handling; CORS and URL config.

### 12.3 `AI_USAGE.md` (short, honest, personal)
Which AI tool(s), for which parts, **where the AI got something wrong and how it was caught and fixed.**
> **Instruction to the AI:** provide your private mistake log as raw material only. I (the human) will review and rewrite this file in my own words. Do not fabricate incidents.

---

## 13. Repository Layout & Commit Plan

```
/
├─ backend/        (src/, tests/, Dockerfile)
├─ frontend/       (src/, Dockerfile, nginx conf, entrypoint script)
├─ scripts/        (hammer script, bonus)
├─ docker-compose.yml
├─ .env.example
├─ README.md
├─ ASSUMPTIONS.md
├─ AI_USAGE.md
└─ prompt.md
```

**Commit rules [SPEC: real history, incremental]:** meaningful messages (Conventional Commits fine), one logical change per commit. Suggested sequence:
1. `chore: init repo, gitignore, skeleton`
2. `feat(backend): project setup, config, DbContext, migrations (advisory-locked)`
3. `feat(backend): create/list/get class endpoints`
4. `feat(backend): hold creation with DB row locking`
5. `feat(backend): get hold, idempotent confirm, cancel, /me/holds`
6. `feat(backend): derived expiry, error middleware, validation, CORS`
7. `test(backend): concurrency, expiry, confirm/cancel, validation`
8. `feat(backend): Dockerfile + health endpoint`
9. `feat(frontend): scaffold, API client, student ID bar, error boundary`
10. `feat(frontend): class list, create class, polling`
11. `feat(frontend): application screen, countdown, pay/cancel`
12. `feat(frontend): my applications, friendly error messages`
13. `build: compose, frontend Dockerfile, runtime API URL config`
14. `test(frontend): vitest tests (bonus)`
15. `feat: multi-process profile + hammer script (bonus)`
16. `docs: README, ASSUMPTIONS, AI_USAGE`

---

## 14. Build Phases (each ends with a verification gate)

| Phase | Work | Verification gate |
|-------|------|-------------------|
| 1 Foundation | Skeleton, backend project, schema + migrations, compose with Postgres, `/health` | `docker compose up` starts db + backend; `/health` OK |
| 2 Classes API | `POST/GET /classes`, `GET /classes/{id}`, validation, error shape | curl all three; bad inputs return Section 7 errors |
| 3 Holds core | Hold creation with `FOR UPDATE`, derived counts, one-per-student, partial unique index, fast path | Quick parallel script shows no oversell |
| 4 Hold lifecycle | `GET /holds/{id}`, confirm, delete, `/me/holds`, optional sweeper | curl every status path from Section 7; empty-body POSTs work; JSON shapes match Section 6 exactly |
| 5 Backend tests | Tests from 10.1 | **All pass.** Fix any bug they reveal first |
| 6 Frontend | API client, ID bar, error boundary, class list/create, application + countdown, my applications, polling | Every 9.3 behavior demonstrated by hand in a real browser (including Cancel/`DELETE` working through CORS) |
| 7 Dockerization | Both Dockerfiles, full compose, runtime URL config, test profiles | Clean `docker compose up --build`; `down` → `up` keeps data; Docker-only test commands work |
| 8 Bonus | `backend2` + hammer script, frontend tests | Hammer shows `held + confirmed <= totalSeats` across both ports |
| 9 Docs | README, ASSUMPTIONS, AI_USAGE; end-to-end walkthrough | Section 15 fully ticked |

---

## 15. Final Acceptance Checklist (verify each before declaring done)

**Contract & rules**
- [ ] All 8 endpoints exist with exact paths, methods, headers, response shapes, and success codes (`201`, `200`, `204`); no field removed or renamed; `status` is uppercase; timestamps end in `Z`.
- [ ] `409` is returned when no seat is available.
- [ ] `held + confirmed <= totalSeats` under heavy parallel load (proven by test).
- [ ] Expired holds free seats with no API call; `GET /holds/{id}` and `/me/holds` report `EXPIRED` once time passes.
- [ ] One active/confirmed application per student per class.
- [ ] Only the owner's non-expired `HELD` hold can be confirmed; confirm is idempotent.
- [ ] `HOLD_DURATION_SECONDS` works (default 600); DB connection string and frontend API URL come from env vars.
- [ ] `docker compose down` → `up` keeps data.

**UI**
- [ ] Create class, apply, pay, cancel, view applications all work purely from the UI (no Postman/DB).
- [ ] Countdown hits zero → `EXPIRED` shown and Pay disabled **without reload**.
- [ ] Class seat numbers refresh every ~4 s **without reload**.
- [ ] Refresh/close-reopen still shows the student's current applications.
- [ ] Every error case (no seats, expired, invalid input, server down) shows a human-readable message; **no blank page, no console-only errors.**

**Delivery**
- [ ] Incremental Git history with meaningful commits.
- [ ] `backend/` and `frontend/` folders, both Dockerfiles, root `docker-compose.yml`.
- [ ] `README.md` (run steps, frontend URL, test steps), `ASSUMPTIONS.md`, `AI_USAGE.md`.
- [ ] At least 3 backend automated tests including a **concurrency test**; tests runnable with Docker only.
- [ ] *(Bonus)* two backend processes verified on one DB; frontend tests; explanation in `ASSUMPTIONS.md`.
- [ ] A fresh clone with only Docker installed: `docker compose up --build` works.

---

## 16. Common Pitfalls to Avoid

- Using `lock` / `SemaphoreSlim` / in-memory counters for seat safety (breaks across processes).
- "Check count, then insert" **without** a database lock (classic oversell race).
- Using `now()` instead of `clock_timestamp()` after a lock wait in write transactions.
- Counting expired-but-unswept holds as occupied.
- Non-idempotent confirm, or confirming an expired/foreign hold.
- Relying only on a background job for expiry; letting `CONFIRMED` holds appear `EXPIRED`.
- Mixing app clock and DB clock.
- `char(17)` for student IDs, or backend rejecting IDs that are not exactly 17 digits (could break API tests).
- C# enum serialized as `0`/`Held` instead of `"HELD"`; timestamps without `Z`.
- `415 Unsupported Media Type` when a test posts with no body or no `Content-Type`.
- CORS that allows only `GET`/`POST` or blocks `X-Student-Id` (UI "Cancel" silently fails in the browser).
- Pool size × number of backend processes exceeding Postgres `max_connections`.
- Two processes running migrations at the same time (use an advisory lock).
- Leaving transactions open on exceptions; unparameterized SQL.
- Hardcoding `localhost:8080` or connection strings in source.
- UI polling that stacks requests or leaks intervals; countdown that needs a reload to flip to `EXPIRED`.
- Blank page on API failure; errors swallowed into `console.log`.
- SPA routes returning 404 on refresh (missing nginx fallback).
- Starting the backend before Postgres is ready.
- Documenting `docker compose down -v`.
- One giant commit at the end.
