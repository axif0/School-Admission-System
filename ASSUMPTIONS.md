# Assumptions & Architecture Decisions

This document records all choices where the assignment specification was silent, following **Section 7** and **Section 12.2** of `prompt.md`.

---

## 1. Pre-Made Decisions (Section 7)

| Topic | Decision | Source | Reason |
|-------|----------|--------|--------|
| Create class success code | `201 Created` | SPEC | Standard RESTful creation status code. |
| No seats on apply | `409 NO_SEATS_AVAILABLE` | SPEC | Required by assignment specification. |
| Cancel success code | `204 NoContent` | SPEC | Resource state transition complete; no body needed. |
| ID format | `c_` + 12-char alphanumeric (classes), `h_` + 12-char (holds) | CHOICE | Unguessable, URL-safe identifiers prefixed for readability and test clarity. |
| Standard error response shape | `{ "error": { "code": "...", "message": "...", "traceId": "..." } }` | CHOICE | Clear machine-readable error codes paired with human-readable explanations and request trace ID; no internal stack traces leaked. |
| Check order for hold actions | 1) header `400` → 2) exists `404` → 3) ownership `403` → 4) state/business rule `409` | CHOICE | Natural defensive order: validate syntax before DB lookups, check ownership before exposing state conflict. |
| Student already applied AND class full | `ALREADY_APPLIED` | CHOICE | Duplicate student check precedes seat count check inside the locked transaction. |
| Missing/blank/invalid `X-Student-Id` | `400 INVALID_STUDENT_ID` | CHOICE | Header must be present, non-blank after trimming, $\le 64$ characters, no control characters. Format is kept lenient on backend to allow API tests with arbitrary test identifiers. |
| Invalid class body | `400 VALIDATION_ERROR` | CHOICE | `name`: 1-100 characters; `totalSeats`: integer between 1 and 100,000. |
| Class/hold not found | `404 NOT_FOUND` | CHOICE | Consistent standard 404 response for any missing entity or malformed ID. |
| Duplicate hold by student in same class | `409 ALREADY_APPLIED` | CHOICE | Enforces rule B4 (at most one active or confirmed hold per student per class). |
| Confirm on expired hold | `409 HOLD_EXPIRED` | CHOICE | Distinguishes expiration from cancellation or ownership errors. |
| Confirm on cancelled hold | `409 HOLD_CANCELLED` | CHOICE | Application was explicitly cancelled. |
| Confirm/Delete foreign hold | `403 NOT_YOUR_HOLD` | CHOICE | Prevents applicants from tampering with other applicants' holds. |
| Confirm an already `CONFIRMED` hold | `200 CONFIRMED`, no change | SPEC | Mandated idempotent confirmation; `confirmedSeats` only increments once. |
| `DELETE` on `HELD` | `204 NoContent`, becomes `CANCELLED` | SPEC | Immediate release of seat reservation. |
| `DELETE` on `CONFIRMED` | `409 ALREADY_CONFIRMED` | CHOICE | Admission fee has been processed; cancellation/refunds are out of scope. |
| `DELETE` on `CANCELLED` or `EXPIRED` | `204 NoContent` | CHOICE | Idempotent cancellation; resource is already inactive. |
| Same student in different classes | Allowed | SPEC | Rule B4 specifies "in a class". Morning and Day shifts are independent classes. |
| Duplicate class names | Allowed | CHOICE | Class ID is the unique identity; schools may run concurrent sections with similar labels. |
| `expiresAt` after confirm | Keep original timestamp | CHOICE | The UI and counting logic ignore `expiresAt` once status is `CONFIRMED`. |
| `GET /holds/{id}` auth header | No header required | SPEC | Unguessable hold IDs; allows status verification. |
| `GET /classes` ordering | Creation time ascending | CHOICE | Predictable order without pagination. |
| Time source | PostgreSQL database clock (`clock_timestamp()`) | CHOICE | Guarantees atomic, synchronized time evaluation across multiple backend instances. |
| Overload / Lock timeout handling | `503 TEMPORARILY_BUSY` | CHOICE | Friendly user-facing notification when row lock waits exceed timeout. |
| UI polling interval | 4 seconds | SPEC/CHOICE | Keeps seat counts live without overwhelming the server. |

---

## 2. Multi-Process Concurrency Safety (Bonus)

### Why the architecture is safe across multiple backend processes:
1. **The Database is the Lock:**
   No in-memory concurrency primitives (`lock`, `SemaphoreSlim`, `ConcurrentDictionary`) are used for seat reservation correctness.
2. **Class-Row `FOR UPDATE` Serialization:**
   When `POST /classes/{id}/holds` is invoked:
   ```sql
   SELECT total_seats FROM classes WHERE id = @classId FOR UPDATE;
   ```
   PostgreSQL enforces an exclusive row-level lock on the specific class row. Any other concurrent request (whether on Backend 1, Backend 2, or further instances) blocks on this lock until the active transaction commits or rolls back.
3. **`clock_timestamp()` vs `now()`:**
   Because a transaction may wait several milliseconds for a lock, `clock_timestamp()` is evaluated *after* acquiring the lock, ensuring fresh timestamps rather than the stale transaction start time returned by `now()`.
4. **Partial Unique Index Safety Net:**
   ```sql
   CREATE UNIQUE INDEX ix_holds_unique_active
   ON holds (class_id, student_id)
   WHERE status IN ('HELD','CONFIRMED');
   ```
   Guarantees at the storage engine level that duplicate active holds can never be inserted even if an unexpected condition arose.

---

## 3. Expiry and Simulated Payment Strategy
- **Derived Expiry:** Expiry correctness does not rely on a scheduled background worker. Every read endpoint (`GET /classes`, `GET /classes/{id}`, `GET /holds/{id}`, `GET /me/holds`) derives hold status using `expires_at > clock_timestamp()`. A lightweight background sweeper runs periodically solely for database cleanup.
- **Simulated Payment:** Clicking "Pay Admission Fee" immediately completes the simulated payment transaction via `POST /holds/{id}/confirm`.

---

## 4. Frontend Runtime Configuration & Clock Skew
- **True Environment Variable (`API_URL`):** Docker nginx container generates `config.js` via an entrypoint script before starting nginx. This allows changing the backend URL at deployment time without rebuilding Vite assets.
- **Clock Skew Compensation:** The frontend calculates the clock difference `offset = serverTime - clientNow` on fetching hold details, ensuring that client machine clock drift does not cause premature or delayed hold expiry.

---

## 5. Security & Performance Enhancements
- **SingleFlight Query Coalescing:** Multiple concurrent requests for `GET /classes` share a single in-flight database query via `ClassQueryCoalescer`, protecting PostgreSQL from read stampedes while guaranteeing read-your-writes consistency across mutations.
- **Configurable Rate Limiting:** Built-in sliding window rate limiter protects endpoints from brute force and denial of service (`RATE_LIMIT_ENABLED=true`).
- **Non-Root Containers:** Backend runs as unprivileged `.NET` user (UID 1654), frontend runs under unprivileged Nginx (UID 101), with read-only root filesystems and explicit resource limits.
