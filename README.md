# School Admission System (Seat-Hold System)

A full-stack school admission system designed to handle heavy concurrency and flash traffic during admission windows. Built with **.NET 8 (C#)**, **PostgreSQL 16**, and **React 18 (TypeScript + Vite)**.

<img width="1151" height="733" alt="image" src="https://github.com/user-attachments/assets/14cadbce-4e80-41d7-939b-e2b8db5b393e" />

---

## How Simultaneous Seat Requests are Handled

When thousands of guardians request seats simultaneously for the same class shift, PostgreSQL's `SELECT ... FOR UPDATE` row-level locking serializes seat allocation transactions on that class row. The first transactions up to `totalSeats` acquire holds, while subsequent requests deterministically receive `409 Conflict` without ever overselling (`held + confirmed <= totalSeats`).

To ensure the database connection pool does not get exhausted under flash traffic, the backend uses two concurrency components before acquiring the row lock:
- **`ClassWriteGate` (Striped Semaphore Array for `O(1)` Bounded Memory):** Uses a fixed 1024-slot striped semaphore array (`MaxConcurrentWritesPerClass = 32` by default) to meter concurrent write transactions per `classId` in an async FIFO queue before checking out a DB connection.
- **`ClassQueryCoalescer` (Monotonic Global Write Versions for Read-Your-Writes Consistency):** Coalesces concurrent in-flight `GET /classes` and `GET /classes/{classId}` queries so hundreds of simultaneous readers share a single DB round-trip, while tracking monotonic global and per-class write versions so any read arriving after a write always fetches fresh data.

```
1000 Simultaneous Write Requests (POST /classes/{classId}/holds)
        |
        v
  ClassWriteGate (1024-slot striped semaphore array, O(1) memory)
        |
        +---- 32 Active Requests ----> Fast-Path Check & PostgreSQL Transaction
        |                                     |
        |                                     v
        |                            SELECT ... FOR UPDATE (Row Lock on classId)
        |                                     |
        |                                     v
        |                            Commit & Bump Write Version
        |                                     |
        |                                     v
        |                            ClassQueryCoalescer (Invalidates stale flights & pushes SSE)
        |
        +---- 968 Waiting in Async Queue (0 DB connections held while waiting)
```

### SQL Example

```sql
BEGIN;
SET LOCAL lock_timeout = '15s';

-- 1. Lock the target class row exclusively
SELECT total_seats
FROM classes
WHERE id = @classId
FOR UPDATE;

-- 2. Sync database clock after acquiring lock
SELECT clock_timestamp();

-- 3. Expire stale holds for this class inside the lock
UPDATE holds
SET status = 'EXPIRED'
WHERE class_id = @classId AND status = 'HELD' AND expires_at <= @now;

-- 4. Check if student already has an active hold/confirmed seat
SELECT 1 FROM holds
WHERE class_id = @classId AND student_id = @studentId AND status IN ('HELD', 'CONFIRMED')
LIMIT 1;

-- 5. Count active seats (held + confirmed <= total_seats)
SELECT COUNT(*)::int FROM holds
WHERE class_id = @classId AND status IN ('HELD', 'CONFIRMED');

-- 6. Insert new hold
INSERT INTO holds (id, class_id, student_id, status, expires_at, created_at)
VALUES (@id, @classId, @studentId, 'HELD', @expiresAt, @now);

COMMIT;
```

Inside the transaction, the target class row is locked exclusively (`FOR UPDATE`), any expired holds for that class are immediately marked `EXPIRED` using `clock_timestamp()`, active seat counts and duplicate student applications are checked, and the new hold is inserted before committing and releasing the lock.

Additionally, stopping the containers with `docker compose down` preserves all database records in the persistent Docker volume (`school_admission_pgdata`).

---

## Running with Docker

### 1. Start the Application
Run the following command in the project root to build and launch PostgreSQL, the backend API, and the React frontend:
```bash
docker compose up -d --build
```

### 2. Service URLs
- **Frontend UI:** [http://localhost:3000](http://localhost:3000)
- **Backend API:** [http://localhost:8080](http://localhost:8080)
- **Health Check:** [http://localhost:8080/health/ready](http://localhost:8080/health/ready)

### 3. Environment Variables
All required settings can be overridden via environment variables when running `docker compose up`:

| Variable | Default | Description |
| :--- | :--- | :--- |
| `ConnectionStrings__Default` | `Host=db;Port=5432;Database=school_admission;Username=postgres;Password=postgres;Maximum Pool Size=50` | PostgreSQL connection string for the backend |
| `HOLD_DURATION_SECONDS` | `600` | Seat hold expiry duration in seconds (default 10 minutes) |
| `API_URL` | `http://localhost:8080` | Backend API base URL injected into the frontend at container startup |
| `CORS_ALLOWED_ORIGINS` | `http://localhost:3000` | Allowed CORS origins for the backend |

Example with custom hold duration (e.g., 60 seconds):
```bash
HOLD_DURATION_SECONDS=60 docker compose up -d
```

### 4. Multi-Process Bonus Mode
To run a second backend instance (`backend2` on port `8081`) sharing the same PostgreSQL database:
```bash
docker compose --profile multi up -d --build
```

### 5. Stop the Application
```bash
docker compose down
```
*(To also remove persistent database volumes: `docker compose down -v`)*

---

## Running Tests

### Via Docker (Recommended)
```bash
# Run backend unit & concurrency integration tests
docker compose run --rm backend-tests

# Run frontend automated tests
docker compose run --rm frontend-tests

# Run API contract smoke test (while services are running)
./scripts/contract-smoke.sh
```

### Locally on Host Machine
* **Backend:**
  ```bash
  dotnet test backend/SchoolAdmission.sln
  ```
  *(Automatically connects to local/Docker PostgreSQL on port `5432`, or uses the thread-safe test store if PostgreSQL is offline).*
* **Frontend:**
  ```bash
  cd frontend
  npm install
  npm test
  ```
