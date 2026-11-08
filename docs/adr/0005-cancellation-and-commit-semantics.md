# ADR 0005: Client Cancellation Propagation and Durable Commit Semantics

## Status
Accepted

## Context
When handling HTTP requests under load, clients may abort connections (browser closed, timeout triggered, user navigated away). ASP.NET Core exposes `HttpContext.RequestAborted` via `CancellationToken`.

If a client aborts during database operations, cancelling indiscriminately can lead to unpredictable partial states:
- If a client disconnects while waiting for a lock, continuing to hold the lock wastes server resources.
- If a client disconnects after the server has successfully verified seat availability, generated an ID, and prepared the database insert, aborting during `CommitAsync` causes a valid transaction to roll back even though the server did all the work, or worse, can lead to ambiguous state if a partial commit happens.

## Decision
We enforce a split cancellation policy:
1. **Cooperative Cancellation During Read & Lock-Wait:**
   Pass the incoming request `CancellationToken` to unlocked pre-checks, row-locking queries (`LockClassAsync`), and clock synchronization queries. If the client disconnects before acquiring the lock, execution terminates early and no lock is held.
2. **Durable Non-Cancellable Commit:**
   Once row invariants have been verified and SQL modifications (`InsertHoldAsync`, `ConfirmHoldAsync`, `CancelHoldAsync`) have been staged, execute `tx.CommitAsync(CancellationToken.None)`.

## Rationale
- Preserves transactional atomicity: once a seat allocation is determined to be valid, the database write must be durable.
- Avoids rolled-back valid reservations due to fleeting mobile TCP disconnects immediately after request dispatch.
- Ensures `tx.RollbackAsync(CancellationToken.None)` executes reliably during exception cleanup without being cut short by client cancellation tokens.

## Consequences
- **Positive:** Unlocked waits are cancelled immediately, freeing database connections; valid allocated holds commit durably without phantom rollbacks.
- **Negative:** If a client drops precisely at the moment of commit, the seat is held until `HOLD_DURATION_SECONDS` expires; this is the intended domain behavior.
