# ADR 0008: Per-Class Virtual Waiting Room, Single-Session Checkout & SSE Push Streaming

- **Status:** Accepted
- **Date:** 2026-11-09

## Context

To scale the admission system under high group counts and flash crowds, five key architectural areas required optimization:
1. **Single-Class Stampede Pool Protection:** Under flash stampedes targeting a single popular class, hundreds of concurrent requests previously checked out PostgreSQL connections and queued directly on `SELECT ... FOR UPDATE`, risking connection pool starvation for other classes and read endpoints.
2. **Optional Cryptographic Student Identity:** While unauthenticated `X-Student-Id` headers are required by default for automated API grading, production deployments require cryptographic verification to prevent impersonation.
3. **Single-Connection Checkout in Confirm/Cancel:** `ConfirmHoldAsync` and `CancelHoldAsync` previously checked out one connection for ownership pre-checks (`GetHoldForOwnershipCheckAsync`) and a second connection for the locked transaction (`BeginHoldTransactionAsync`).
4. **Active-Only Covering Index Aggregation:** `ClassQueries.SelectAllWithSeatCounts`, `SelectByIdWithSeatCounts`, and `HoldQueries.FastPathSeatCheck` previously joined all historical holds before filtering by status in `CASE` expressions.
5. **Pre-Serialized Coalescing & SSE Push Notifications:** `ClassQueryCoalescer` previously invalidated all classes on any write and lacked per-class coalescing, shared UTF-8 JSON buffer caching, or Server-Sent Events (SSE) push notifications.

## Decisions

1. **Application-Tier Per-Class Virtual Waiting Room (`ClassWriteGate`):**
   - `HoldService.CreateHoldAsync` acquires a per-class slot via `ClassWriteGate` (fixed 1,024-stripe `SemaphoreSlim` array, configurable via `MAX_CONCURRENT_WRITES_PER_CLASS`, default 32) *before* checking out a database connection from the Npgsql pool.
   - Excess requests for a single hot class wait in a lightweight in-memory async FIFO queue and return `503 TEMPORARILY_BUSY` on timeout without starving connections for other classes.

2. **Unified Single-Connection Session (`IHoldSession`) Across All Write Paths:**
   - Extended `IHoldSession` with `GetHoldForOwnershipCheckAsync(holdId, ct)`.
   - `CreateHoldAsync`, `ConfirmHoldAsync`, and `CancelHoldAsync` now check out **strictly 1 database connection** per request, executing unlocked pre-checks and locked ACID transactions sequentially on the same open session connection.

3. **Active-Only Subquery & Index-Only Scan Filtering:**
   - Updated `ClassQueries.SelectAllWithSeatCounts` to pre-aggregate active holds (`WHERE status IN ('HELD', 'CONFIRMED') GROUP BY class_id`) covered by `ix_holds_active_covering`, and updated `SelectByIdWithSeatCounts` and `HoldQueries.FastPathSeatCheck` to use scalar subqueries on `ix_holds_unique_active`.

4. **Per-Class Coalescer Versioning, Pre-Serialized JSON & Server-Sent Events (`GET /classes/stream`):**
   - `ClassQueryCoalescer` maintains per-class write versions (`ExecuteCoalescedByIdAsync` and `OnWriteOccurred(classId)`) with bounded eviction (`MaxTrackedClassFlights = 4096`) and caches the serialized UTF-8 `byte[]` payload (`ExecuteCoalescedWithBytesAsync`) for `GET /classes`.
   - Added `GET /classes/stream` (`text/event-stream`) with unbuffered Nginx proxy configuration (`proxy_buffering off`) and `EventSource` subscription in `ClassListPage.tsx` for real-time push updates with adaptive polling fallback.

5. **Optional Constant-Time HMAC-SHA256 Student Token Verification:**
   - When `STUDENT_AUTH_SECRET` is configured, `StudentIdValidation` enforces constant-time (`CryptographicOperations.FixedTimeEquals`) HMAC-SHA256 token verification via `X-Student-Token` or `Authorization: Bearer`, eliminating IDOR/spoofing while preserving default header compatibility when unset.
