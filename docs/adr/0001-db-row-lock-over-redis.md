# ADR 0001: PostgreSQL Row Locks Over Redis for Concurrency & Invariants

## Status
Accepted

## Context
The School Admission System must strictly guarantee that:
1. A class never exceeds its allocated capacity: `heldSeats + confirmedSeats <= totalSeats`.
2. A student can have at most one active hold or confirmed seat per class.
3. Holds automatically expire after `HOLD_DURATION_SECONDS`.

Auditors suggested using Redis counters with `DECR` / distributed Redlock to handle concurrency. We evaluated whether Redis or PostgreSQL row-level locks best serve this requirement.

## Decision
We chose PostgreSQL row-level locking (`SELECT ... FOR UPDATE` on the parent `classes` record within a serializable transaction) as the single source of truth for seat allocation. We rejected Redis seat counters and distributed locks for core invariant enforcement.

## Rationale
1. **Multi-Key Transactional Atomicity:**
   Seat reservation requires verifying *two* distinct invariants atomically:
   - Total available capacity (`activeCount < totalSeats`).
   - Per-student exclusivity (`NOT EXISTS active hold for student in this class`).
   While a Redis `DECR` can decrement a single counter atomically, verifying whether a specific student already holds a seat requires a second key or Lua script. In failure scenarios (e.g., Redis restart, network partitions, crash during Lua execution), Redis counter state desynchronizes from PostgreSQL persistent storage.
2. **Crash Consistency & No Split-Brain:**
   Redis is in-memory by design. Even with AOF `fsync=always`, Redis persistence lags behind relational ACID durability. If Redis crashes after decrementing a seat counter but before the PostgreSQL transaction commits, the seat is permanently lost (ghost leak). If PostgreSQL rolls back, compensating Redis `INCR` commands can fail or be reordered under concurrent load.
3. **Derived Real-Time Expiry:**
   With PostgreSQL, expired holds naturally vacate seats without background polling through time-aware queries:
   ```sql
   WHERE status = 'CONFIRMED' OR (status = 'HELD' AND expires_at > now())
   ```
   Redis TTLs expire keys, but key expiration events are pub/sub notifications that are not guaranteed to be delivered or processed in order.
4. **Empirical Performance Sufficiency:**
   PostgreSQL 16 handles row-level lock contention on SSD storage with sub-millisecond query execution. Our k6 benchmark proved that PostgreSQL handles 500 concurrent VUs hammering a 10-seat class with **0.00% errors**, **zero overbooking**, and P95 latency under 2.03s.

## Consequences
- **Positive:** Absolute correctness; zero overbooking risk; zero desynchronization between cache and database; simpler operational topology (no Redis cluster to maintain, monitor, and failover).
- **Negative:** Heavy write spikes on a single class serialize on that specific row lock. Mitigated via fast-path unlocked read pre-checks, short lock hold times (< 5ms), and 15s lock timeouts returning graceful HTTP 503 instead of thread starvation.
