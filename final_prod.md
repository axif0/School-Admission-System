# School Admission System — Production & Architecture Documentation (`final_prod.md`)

This comprehensive production document details the complete end-to-end architecture, technical implementation, concurrency guarantees, endpoint behaviors, frontend features, and Docker containerization of the **School Admission System**.

---

## 📑 Table of Contents

1. [Executive Summary & High-Level Architecture](#1-executive-summary--high-level-architecture)
2. [Database Schema & Migration Design](#2-database-schema--migration-design)
3. [Backend Implementation & Function-Level Breakdown](#3-backend-implementation--function-level-breakdown)
   - 3.1 [Dependency Injection & Program Pipeline](#31-dependency-injection--program-pipeline)
   - 3.2 [Advisory-Locked Migration Engine](#32-advisory-locked-migration-engine)
   - 3.3 [HoldService Core Concurrency Engine](#33-holdservice-core-concurrency-engine)
   - 3.4 [ClassService Functions](#34-classservice-functions)
   - 3.5 [Validation & Error Handling Middleware](#35-validation--error-handling-middleware)
   - 3.6 [Derived Expiry & Background Cleanup Sweeper](#36-derived-expiry--background-cleanup-sweeper)
4. [API Contract & Specifications](#4-api-contract--specifications)
5. [Frontend Implementation & Component Details](#5-frontend-implementation--component-details)
   - 5.1 [Runtime Environment Variable Injection](#51-runtime-environment-variable-injection)
   - 5.2 [17-Digit Birth Registration Bar Component](#52-17-digit-birth-registration-bar-component)
   - 5.3 [Class List & Live Polling Engine](#53-class-list--live-polling-engine)
   - 5.4 [Application Page with Live Countdown & Clock Skew Compensation](#54-application-page-with-live-countdown--clock-skew-compensation)
   - 5.5 [My Applications Page](#55-my-applications-page)
   - 5.6 [Bilingual i18n System](#56-bilingual-i18n-system)
6. [Docker & Containerization Architecture](#6-docker--containerization-architecture)
   - 6.1 [Multi-Stage Backend Dockerfile](#61-multi-stage-backend-dockerfile)
   - 6.2 [Multi-Stage Frontend Dockerfile & Nginx SPA Routing](#62-multi-stage-frontend-dockerfile--nginx-spa-routing)
   - 6.3 [Docker Compose Services & Named Volume Persistence](#63-docker-compose-services--named-volume-persistence)
   - 6.4 [Multi-Process Bonus Architecture & Hammer Script](#64-multi-process-bonus-architecture--hammer-script)
7. [Automated Test Suite & Verification](#7-automated-test-suite--verification)

---

## 1. Executive Summary & High-Level Architecture

The School Admission System is built to withstand high concurrency and sudden flash traffic during school admission windows (thousands of simultaneous applications).

```
+-----------------------------------------------------------------------------------+
|                                  React 18 SPA                                     |
|           (Vite + TypeScript, Nginx reverse proxy, Port 3000)                     |
+-----------------------------------------------------------------------------------+
                          |                                     |
             HTTP Calls   | [X-Student-Id: 20152691234567890]   | (4-sec polling)
                          v                                     v
+---------------------------------------+ +-----------------------------------------+
|      ASP.NET Core Web API             | |      ASP.NET Core Web API               |
|      (Backend Instance 1, Port 8080)  | |      (Backend Instance 2, Port 8081)    |
+---------------------------------------+ +-----------------------------------------+
                    \                                         /
                     \  Npgsql Connection Pool (Max Size 50) /
                      v                                     v
+-----------------------------------------------------------------------------------+
|                                 PostgreSQL 16                                     |
|  - Exclusive Row Lock (SELECT ... FOR UPDATE)                                     |
|  - Atomic Clock (clock_timestamp())                                               |
|  - Partial Unique Index (UNIQUE(class_id, student_id) WHERE status IN ('HELD',...)|
|  - Named Volume Persistence (school_admission_pgdata)                             |
+-----------------------------------------------------------------------------------+
```

### The Invariant
$$\text{heldSeats} + \text{confirmedSeats} \le \text{totalSeats}$$
This invariant is mathematically and structurally guaranteed at all times across any number of backend instances through database row locking.

---

## 2. Database Schema & Migration Design

The database schema is defined with strict PostgreSQL data types:

### Table: `classes`
```sql
CREATE TABLE classes (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    total_seats INTEGER NOT NULL CHECK (total_seats > 0),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL
);
```

### Table: `holds`
```sql
CREATE TABLE holds (
    id TEXT PRIMARY KEY,
    class_id TEXT NOT NULL REFERENCES classes(id),
    student_id VARCHAR(64) NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('HELD', 'CONFIRMED', 'EXPIRED', 'CANCELLED')),
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    confirmed_at TIMESTAMP WITH TIME ZONE NULL,
    cancelled_at TIMESTAMP WITH TIME ZONE NULL
);

-- Compound index for rapid seat counting and derived expiry lookups
CREATE INDEX ix_holds_class_status_expires ON holds (class_id, status, expires_at);

-- Index for instant lookup of student applications
CREATE INDEX ix_holds_student_id ON holds (student_id);

-- Safety net constraint preventing duplicate active holds per class
CREATE UNIQUE INDEX ix_holds_unique_active ON holds (class_id, student_id)
WHERE status IN ('HELD', 'CONFIRMED');
```

---

## 3. Backend Implementation & Function-Level Breakdown

The backend is built using C# on .NET 8 (ASP.NET Core Minimal APIs + Entity Framework Core + Raw Parameterized Npgsql SQL for lock serialization).

### 3.1 Dependency Injection & Program Pipeline (`Program.cs`)
- **Connection Pooling:** Configures Npgsql connection pool with `Maximum Pool Size=50`.
- **CORS Configuration:** Allows all HTTP methods (`GET`, `POST`, `DELETE`, `OPTIONS`) and custom headers (`X-Student-Id`). This is critical because `DELETE` and custom headers trigger CORS preflight `OPTIONS` requests from the browser.
- **JSON Serialization:** Uses `JsonNamingPolicy.CamelCase`, converts UTC DateTime values ending with `Z`.
- **Health Check (`GET /health`):** Performs `SELECT 1` against the database and returns `{"status":"ok"}` with status code `200` or `503 Service Unavailable`.

### 3.2 Advisory-Locked Migration Engine (`DatabaseMigrator.cs`)
When multiple backend instances boot simultaneously, standard EF Core migration execution can encounter race conditions.
- **Function `MigrateAsync(IServiceProvider services, ILogger logger)`:**
  1. Opens an explicit Npgsql connection.
  2. Executes `SELECT pg_advisory_lock(123456789)`. Only one process acquires the lock; other processes wait.
  3. Executes `db.Database.MigrateAsync()`.
  4. Runs `CREATE UNIQUE INDEX IF NOT EXISTS ix_holds_unique_active`.
  5. Executes `SELECT pg_advisory_unlock(123456789)` in a `finally` block.
  6. Includes retry logic with a 2-second backoff up to 30 attempts while PostgreSQL is initializing.

### 3.3 HoldService Core Concurrency Engine (`HoldService.cs`)

#### A. `CreateHoldAsync(string classId, string studentId)`
This is the core concurrency-critical function in the system:
1. **Unlocked Fast-Path Pre-Check:**
   Executes an unlocked read of the class's current active seat count:
   ```sql
   SELECT c.total_seats,
          COALESCE(SUM(CASE WHEN h.status IN ('HELD','CONFIRMED')
               AND (h.status='CONFIRMED' OR h.expires_at > now()) THEN 1 ELSE 0 END), 0)::int
   FROM classes c
   LEFT JOIN holds h ON h.class_id = c.id
   WHERE c.id = @classId
   GROUP BY c.total_seats
   ```
   If `currentCount >= totalSeats`, throws `AppException.NoSeatsAvailable()` (HTTP 409) immediately without locking the database row, protecting the lock queue during bursts.
2. **Database Row Lock Acquisition (`FOR UPDATE`):**
   Starts an explicit database transaction and sets `SET LOCAL lock_timeout = '15s'`.
   ```sql
   SELECT total_seats FROM classes WHERE id = @classId FOR UPDATE
   ```
   This serializes every application targeting this specific class across all backend processes. If the row does not exist, throws `404 NOT_FOUND`.
3. **Database Clock Retrieval (`clock_timestamp()`):**
   Reads `SELECT clock_timestamp()`. `clock_timestamp()` reflects current wall-clock time after lock acquisition, unlike `now()` which returns transaction start time.
4. **Lazy Status Cleanup:**
   ```sql
   UPDATE holds SET status='EXPIRED'
   WHERE class_id=@classId AND status='HELD' AND expires_at <= @now
   ```
   Frees expired holds immediately before checking capacity or student uniqueness.
5. **Student Single Application Rule (B4):**
   Checks if the student already has an active hold:
   ```sql
   SELECT 1 FROM holds
   WHERE class_id=@classId AND student_id=@studentId AND status IN ('HELD','CONFIRMED')
   LIMIT 1
   ```
   If found, throws `AppException.AlreadyApplied()` (HTTP 409).
6. **Seat Capacity Check:**
   Counts active rows:
   ```sql
   SELECT COUNT(*)::int FROM holds
   WHERE class_id=@classId AND status IN ('HELD','CONFIRMED')
   ```
   If `activeCount >= totalSeats`, throws `AppException.NoSeatsAvailable()` (HTTP 409).
7. **Insertion:**
   Inserts the new hold with `expires_at = now.AddSeconds(HOLD_DURATION_SECONDS)`.
8. **Commit:**
   Commits transaction and returns `CreateHoldResponse`.

#### B. `ConfirmHoldAsync(string holdId, string studentId)`
1. Looks up the hold's `class_id` and `student_id`.
2. Starts transaction and acquires `FOR UPDATE` lock on the class row.
3. Retrieves `clock_timestamp()`.
4. **Ownership Verification:** If `student_id != currentStudentId`, throws `403 NOT_YOUR_HOLD`.
5. **Idempotence Check:** If hold is already `CONFIRMED`, returns `200 OK` without modifying counters.
6. **State Validation:**
   - If `status == CANCELLED` $\to$ throws `409 HOLD_CANCELLED`.
   - If `status == EXPIRED` or `expires_at <= now` $\to$ updates status to `EXPIRED` and throws `409 HOLD_EXPIRED`.
7. **Transition:** Sets `status = 'CONFIRMED'` and `confirmed_at = now`. Commits transaction.

#### C. `CancelHoldAsync(string holdId, string studentId)`
1. Locks the class row `FOR UPDATE`.
2. Verifies ownership; foreign student $\to$ throws `403 NOT_YOUR_HOLD`.
3. If already `CONFIRMED` $\to$ throws `409 ALREADY_CONFIRMED`.
4. If already `CANCELLED` or `EXPIRED` $\to$ returns HTTP `204 NoContent` (idempotent no-op).
5. If `HELD` $\to$ updates `status = 'CANCELLED'`, `cancelled_at = now`, immediately freeing the seat reservation.

#### D. `GetHoldAsync(string holdId)` & `GetMyHoldsAsync(string studentId)`
- Reads hold records and dynamically derives hold status: if `status == 'HELD'` and `expires_at <= now`, maps to `EXPIRED` in the response DTO.

### 3.4 ClassService Functions (`ClassService.cs`)
- `CreateAsync(CreateClassRequest req)`: Validates name length (1-100 chars) and seat range (1 to 100,000), inserts new record, and returns `201 Created`.
- `GetAllAsync()`: Returns a list of all classes with calculated `heldSeats`, `confirmedSeats`, and `availableSeats` in a single SQL query:
  ```sql
  SELECT c.id, c.name, c.total_seats,
         COALESCE(SUM(CASE WHEN h.status = 'HELD' AND h.expires_at > now() THEN 1 ELSE 0 END), 0)::int,
         COALESCE(SUM(CASE WHEN h.status = 'CONFIRMED' THEN 1 ELSE 0 END), 0)::int
  FROM classes c
  LEFT JOIN holds h ON h.class_id = c.id
  GROUP BY c.id, c.name, c.total_seats, c.created_at
  ORDER BY c.created_at ASC
  ```
- `GetByIdAsync(string classId)`: Fetches a single class's seat statistics with the same counting query.

### 3.5 Validation & Error Handling Middleware
- **`StudentIdValidation.cs`:** Validates `X-Student-Id` header. Checks: present, non-blank after trimming, $\le 64$ characters, no control characters. Format is deliberately lenient on the backend so automated tests with arbitrary IDs pass.
- **`GlobalExceptionMiddleware.cs`:**
  - Catches `AppException` $\to$ returns exact error JSON:
    ```json
    { "error": { "code": "NO_SEATS_AVAILABLE", "message": "No seats are left in this class." } }
    ```
  - Catches DB connection / lock timeouts $\to$ returns `503 TEMPORARILY_BUSY`.
  - Catches unhandled exceptions $\to$ logs stack trace internally and returns generic `500 INTERNAL_ERROR` without exposing server internals.

### 3.6 Derived Expiry & Background Cleanup Sweeper (`HoldExpirySweeper.cs`)
- Expiry correctness is strictly derived on reads; it does not depend on a background scheduler.
- A lightweight `BackgroundService` runs every 5 seconds executing `UPDATE holds SET status='EXPIRED' WHERE status='HELD' AND expires_at <= clock_timestamp()`. Safe across multiple processes.

---

## 4. API Contract & Specifications

| # | HTTP Method & Path | Headers | Success | Error Codes | Description |
|---|--------------------|---------|---------|-------------|-------------|
| 1 | `POST /classes` | None | `201 Created` | `400 VALIDATION_ERROR` | Create a new class shift |
| 2 | `GET /classes` | None | `200 OK` | None | List classes with seat counts |
| 3 | `GET /classes/{classId}` | None | `200 OK` | `404 NOT_FOUND` | Get single class seat breakdown |
| 4 | `POST /classes/{classId}/holds` | `X-Student-Id` | `201 Created` | `400`, `404`, `409 NO_SEATS_AVAILABLE`, `409 ALREADY_APPLIED` | Hold a seat for an applicant |
| 5 | `GET /holds/{holdId}` | None | `200 OK` | `404 NOT_FOUND` | View state of application |
| 6 | `POST /holds/{holdId}/confirm` | `X-Student-Id` | `200 OK` | `400`, `403 NOT_YOUR_HOLD`, `404`, `409 HOLD_EXPIRED`, `409 HOLD_CANCELLED` | Pay fee and confirm admission |
| 7 | `DELETE /holds/{holdId}` | `X-Student-Id` | `204 NoContent` | `400`, `403 NOT_YOUR_HOLD`, `404`, `409 ALREADY_CONFIRMED` | Cancel application |
| 8 | `GET /me/holds` | `X-Student-Id` | `200 OK` | `400 INVALID_STUDENT_ID` | View all applications for student |
| 9 | `GET /health` | None | `200 OK` | `503 SERVICE_UNAVAILABLE` | Health check probe |

---

## 5. Frontend Implementation & Component Details

The frontend is built with React 18, Vite, and TypeScript.

### 5.1 Runtime Environment Variable Injection (`config.js`)
Standard Vite builds bake environment variables at compile-time. To allow `API_URL` to be configurable at runtime when deploying Docker containers:
1. Nginx startup executes `/docker-entrypoint-custom.sh`.
2. The script substitutes the `API_URL` environment variable into `/usr/share/nginx/html/config.js`:
   ```javascript
   window.__APP_CONFIG__ = { API_URL: "http://localhost:8080" };
   ```
3. `index.html` loads `<script src="/config.js"></script>` first.
4. `api/client.ts` resolves:
   `window.__APP_CONFIG__.API_URL` $\to$ `import.meta.env.VITE_API_URL` $\to$ `'http://localhost:8080'`.

### 5.2 17-Digit Birth Registration Bar Component (`StudentIdBar.tsx`)
- Placed prominently at the top of the interface.
- Strictly validates 17 numeric digits: `/^\d{17}$/`.
- Persists entered Student ID to browser `localStorage` under `school_admission_student_id`.
- Includes a **"⚡ ডেমো আইডি বসান" / "Fill Demo ID"** button to populate a valid 17-digit ID with one click during testing.

### 5.3 Class List & Live Polling Engine (`ClassListPage.tsx`)
- Displays all classes with their total, held, confirmed, and available seat counts.
- Displays a visual capacity progress bar indicating seat saturation.
- **Polling Loop:** Auto-refreshes data every 4 seconds. Polling automatically pauses when tab is hidden (`document.visibilityState !== 'visible'`). Overlapping requests are prevented using ref locks.
- **Button Logic:**
  - Disabled if available seats == 0 (labeled "সিট পূর্ণ" / "Full").
  - Disabled if Student ID is invalid.
  - If student already holds a seat in this class, replaces "Apply" button with a direct link to **"আপনার আবেদন (HELD/CONFIRMED)"**.

### 5.4 Application Page with Live Countdown & Clock Skew Compensation (`ApplicationPage.tsx`)
- Computes clock skew between server and client:
  $$\text{offset} = \text{serverTime} - \text{clientNow}$$
  $$\text{remainingSeconds} = \frac{\text{expiresAt} - (\text{clientNow} + \text{offset})}{1000}$$
- Updates every second.
- When timer hits 0:
  - Automatically transitions status badge to **`EXPIRED`** without requiring a page reload.
  - Disables the **"ভর্তি ফি দিন" (Pay Admission Fee)** button immediately.
  - Automatically reconciles state with backend.
- Displays "ভর্তি নিশ্চিত হয়েছে!" banner once confirmed and hides countdown.

### 5.5 My Applications Page (`MyApplicationsPage.tsx`)
- Lists all applications associated with the currently entered student ID.
- Displays application IDs, class names, status badges, and quick links to review or manage each hold.

### 5.6 Bilingual i18n System (`messages.ts`)
- Complete language dictionary supporting Bengali (`bn`) and English (`en`).
- Toggle button on top navbar allows instant switching without data loss.

---

## 6. Docker & Containerization Architecture

### 6.1 Multi-Stage Backend Dockerfile (`backend/Dockerfile`)
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["SchoolAdmission.sln", "./"]
COPY ["src/SchoolAdmission.Api/SchoolAdmission.Api.csproj", "src/SchoolAdmission.Api/"]
COPY ["tests/SchoolAdmission.Tests/SchoolAdmission.Tests.csproj", "tests/SchoolAdmission.Tests/"]
COPY . .
RUN dotnet restore
RUN dotnet build -c Release

FROM build AS test
WORKDIR /src/tests/SchoolAdmission.Tests
ENTRYPOINT ["dotnet", "test", "--no-build", "-c", "Release", "--logger:console;verbosity=normal"]

FROM build AS publish
WORKDIR /src/src/SchoolAdmission.Api
RUN dotnet publish -c Release -o /app/publish --no-build

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SchoolAdmission.Api.dll"]
```

### 6.2 Multi-Stage Frontend Dockerfile & Nginx SPA Routing (`frontend/Dockerfile`)
```dockerfile
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM build AS test
WORKDIR /app
CMD ["npm", "test"]

FROM nginx:alpine AS final
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
COPY entrypoint.sh /docker-entrypoint-custom.sh
RUN chmod +x /docker-entrypoint-custom.sh
EXPOSE 80
ENTRYPOINT ["/docker-entrypoint-custom.sh"]
CMD ["nginx", "-g", "daemon off;"]
```
Nginx config includes SPA fallback `try_files $uri $uri/ /index.html;` so page refreshes on client routes (e.g. `/my-applications`, `/holds/h_xxx`) do not return 404.

### 6.3 Docker Compose Services (`docker-compose.yml`)
1. **`db` (Postgres 16):**
   - Flags: `postgres -c max_connections=200`.
   - Volume: `school_admission_pgdata` named volume guarantees persistence across restarts.
   - Healthcheck: `pg_isready -U postgres -d school_admission`.
2. **`backend`:**
   - Depends on `db (condition: service_healthy)`.
   - Ports: `8080:8080`.
3. **`frontend`:**
   - Ports: `3000:80`.
   - Env: `API_URL=http://localhost:8080`.
4. **`backend2` (Profile: `multi`):**
   - Independent second backend process on port `8081:8080` targeting the same database.
5. **`backend-tests` & `frontend-tests` (Profile: `test`):**
   - Automated testing targets executable with Docker only.

### 6.4 Multi-Process Bonus Architecture & Hammer Script (`scripts/hammer.sh`)
- Boots two separate backend instances (`:8080` and `:8081`) sharing a single database.
- Fires 100 simultaneous parallel requests alternating between Backend 1 and Backend 2 against a class with 15 seats.
- **Verified Output:**
  ```
  Success (201 Created): 15
  Conflict (409 No Seats): 85
  Class Status: Total: 15, Held: 15, Confirmed: 0, Available: 0
  ✅ Invariant preserved! Exactly 15 holds succeeded across both backends.
  ```

---

## 7. Automated Test Suite & Verification

| Test Class | Test Name | Invariant Checked |
|------------|-----------|-------------------|
| `ConcurrencyTests` | `NoOverselling_UnderHeavyParallelLoad_NeverExceedsTotalSeats` | Fires 150 parallel requests for 10 seats $\to$ exactly 10 $\times$ 201 Created, 140 $\times$ 409 Conflict. |
| `ConcurrencyTests` | `SameStudent_ParallelRequests_OnlyOneSucceeds` | 20 parallel requests with same student ID $\to$ exactly 1 $\times$ 201 Created, 19 $\times$ `409 ALREADY_APPLIED`. |
| `HoldLifecycleTests`| `ExpiryFreesSeat_AutomaticallyWithoutApiCall` | Sets `HOLD_DURATION_SECONDS=1`, waits 2 seconds $\to$ status derived as `EXPIRED`, available seats restored. |
| `HoldLifecycleTests`| `ConfirmRules_ValidHold_Idempotent_ExpiredAndForeignRejected` | Confirm succeeds; repeated confirm returns 200 without double counting; foreign student returns 403. |
| `HoldLifecycleTests`| `CancelRules_HeldFreed_ConfirmedRejected_IdempotentOnCancelled` | Cancel frees held seat (204); second cancel returns 204; cancel on confirmed returns 409. |
| `ValidationTests` | `MissingOrBlankStudentId_Returns400InvalidStudentId` | Header missing or whitespace-only returns `400 INVALID_STUDENT_ID`. |
| `ValidationTests` | `InvalidClassBody_Returns400ValidationError` | Seat count $\le 0$ or blank name returns `400 VALIDATION_ERROR`. |
| `frontend.test.tsx`| `Validates 17-digit student id and renders error state` | Vitest component test for 17-digit constraint and error indicators. |
| `frontend.test.tsx`| `Maps error codes to friendly human language` | Verifies translation dictionary across Bengali and English. |

---

## 🏁 Summary of Verified URLs

- **Frontend User Interface:** [http://localhost:3000](http://localhost:3000)
- **Primary Backend Instance:** [http://localhost:8080](http://localhost:8080)
- **Secondary Backend Instance (Multi Profile):** [http://localhost:8081](http://localhost:8081)
- **API Health Endpoint:** [http://localhost:8080/health](http://localhost:8080/health)
