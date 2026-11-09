# ADR 0003: Polling with In-Flight Coalescing Over Server-Sent Events (SSE)

## Status
Superseded by [ADR 0008](0008-sse-and-per-class-concurrency-gate.md) (SSE push streaming added alongside adaptive polling and in-flight coalescing)

## Context
The specification requires that seat counts update every few seconds on the UI without manual page reloads (Section 1.5). Auditors recommended evaluating WebSockets or Server-Sent Events (SSE) instead of periodic HTTP polling.

## Decision
We chose client-side polling (interval 5s with ±20% jitter and exponential backoff on error) paired with server-side in-flight query coalescing (`ClassQueryCoalescer` / SingleFlight pattern). We rejected WebSockets and SSE for the current scale.

## Rationale
1. **Low Cardinality & Small Aggregate Footprint:**
   The school admissions catalog has relatively low cardinality (typically 10 to 50 classes). A single `GET /classes` payload is under 4 KB.
2. **Elimination of Database Read Storms via Coalescing:**
   A major criticism of client polling is the "thundering herd" problem when thousands of clients poll simultaneously. We solved this on the backend with `ClassQueryCoalescer`:
   - When 1,000 clients request `GET /classes` at the same second, only **one single SQL query** runs against PostgreSQL; the other 999 callers await the same in-flight `Task` and receive the same response.
   - Read-your-writes consistency is preserved via a monotonic version counter: whenever a write (hold, confirm, cancel, create) commits, the version increments and any subsequent caller is guaranteed to execute a fresh database query rather than reuse stale in-flight results.
3. **Operational Overhead of Persistent Connections:**
   Persistent connections (SSE / WebSockets) require keeping 10,000+ TCP sockets and file descriptors open across edge proxies and load balancers. In a multi-replica deployment, push notifications require a distributed pub/sub broker (e.g. Redis Pub/Sub, NATS, or RabbitMQ) just to broadcast seat changes to all edge servers.
4. **Resilience & Stateless Failover:**
   Stateless HTTP polling naturally tolerates backend rolling updates, network blips, and mobile sleeping tabs without reconnection storms or connection state recovery handshakes.

## Consequences
- **Positive:** Rock-solid reliability; zero stateful connection management; database query volume reduced by up to 99% under burst polling; trivial horizontal scaling behind any HTTP edge proxy.
- **Negative:** Up to 4–5 second latency between a seat confirmation and another student's UI reflecting the change. Acceptable per specification.
