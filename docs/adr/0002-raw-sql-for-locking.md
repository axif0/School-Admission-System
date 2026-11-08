# ADR 0002: Use of Explicit Parameterized SQL for Concurrency Locking Path

## Context
In `feedback.md` §2.2 and §4.1, the audit observed that business services bypassed EF Core change tracking and executed raw ADO.NET SQL commands directly against PostgreSQL. The audit recommended either fully adopting EF Core LINQ queries or properly isolating SQL into a dedicated data access layer.

## Decision
We decided to **retain explicit SQL statements for all locking and transaction-critical paths**, but **strictly isolate all SQL strings inside dedicated store abstractions** (`IHoldStore`, `IClassStore`, `IHoldTransaction`, `Data/Sql/HoldQueries.cs`, and `Data/Sql/ClassQueries.cs`).

We reject converting the concurrency locking path to EF Core LINQ queries for the following technical reasons:

1. **Deterministic Locking Semantics:**
   PostgreSQL `SELECT ... FOR UPDATE` row locks, lock timeouts (`SET LOCAL lock_timeout`), and atomic partial conditional expressions (`WHERE status IN ('HELD', 'CONFIRMED')`) are first-class database primitives. EF Core's LINQ provider does not natively generate clean row-level lock hints across all query projections without fragile extensions.
2. **Clock Synchronization:**
   PostgreSQL's `clock_timestamp()` ensures absolute clock synchronization against the database server clock across all distributed backend nodes. Entity Framework translation of `DateTime.UtcNow` evaluates in application memory on the local node, exposing the system to distributed clock drift bugs.
3. **Partial Unique Indexes:**
   The database-level invariant is enforced by PostgreSQL's partial unique index:
   ```sql
   CREATE UNIQUE INDEX IF NOT EXISTS ix_holds_unique_active
   ON holds (class_id, student_id)
   WHERE status IN ('HELD','CONFIRMED');
   ```
   Executing explicit SQL ensures transparent alignment between database indexes and transactional updates.
4. **Separation of Concerns Restored:**
   Rather than rewriting the SQL, we extracted the stores and unit of work into clean interfaces. Domain services (`HoldService`, `ClassService`) now depend purely on abstractions (`IHoldStore`, `IClassStore`), and domain status rules are encapsulated in `HoldStateMachine`.

## Schema Synchronization Verification
To prevent SQL string drift from entity schema changes, integration test suites (`ConcurrencyTests`, `HoldLifecycleTests`, `ValidationTests`) execute against a live PostgreSQL test container on every build and CI run, exercising every single query against the real database schema.
