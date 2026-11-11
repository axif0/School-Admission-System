# AI Usage & Verification Log

## 1. AI Assistance Overview
- **Models / Tools:** Claude Opus & Gemini 3.8 Flash via Antigravity Agentic Assistant.
- **Scope:** Backend API scaffolding (.NET 8 + PostgreSQL), React 18 TypeScript UI components, Docker Compose setup, and drafting k6 load-testing scenarios.

---

## 2. Load Testing & Verification with AI

To verify that the system handles flash admission traffic without overselling seats (`held + confirmed <= totalSeats`), I prepared and executed automated testing and load-testing scripts (`loadtest/stampede.js`, `loadtest/mixed.js`, `loadtest/soak.js`, `scripts/hammer.sh`, and `scripts/contract-smoke.sh`) with AI assistance and verified all results on my local test environment.

### Test Environment
- **Host OS:** Linux 6.8.0-48-generic x86_64
- **CPU / Architecture:** 12-core AMD Ryzen / Intel x86_64
- **RAM:** 16 GB
- **Load Generator:** Grafana k6 v2.3.0 (Official Docker Image `grafana/k6:latest`)
- **Runtime:** .NET 8 ASP.NET Core Kestrel + PostgreSQL 16 Alpine

### How AI Was Used in Load Testing & How I Verified It
1. **Scenario Generation & Teardown Invariant Checks:**
   - I used AI to help structure the k6 scenarios (`stampede.js` for 500 concurrent VUs competing for 120 seats, `mixed.js` for concurrent polling and booking, and `soak.js` for sustained read load) along with shell verification scripts (`scripts/hammer.sh` and `scripts/contract-smoke.sh`).
   - **Mistake Caught & Fixed in k6 Script:** Initially, AI-generated k6 checks treated `409 Conflict` responses as failed HTTP requests (`http_req_failed`), causing false failure rates when seats ran out. I caught this during test runs and configured `http.expectedStatuses(201, 409)` along with a `teardown()` verification step that queries `GET /classes/{classId}` after the stampede to assert `heldSeats + confirmedSeats <= totalSeats`.
2. **Database Connection Pool Exhaustion Under Stampede:**
   - When running the 500-VU stampede test against a single class, I noticed high lock contention waiting on `SELECT ... FOR UPDATE`. I worked with AI to introduce a per-class concurrency gate (`ClassWriteGate`) and request coalescing (`ClassQueryCoalescer`) for `GET /classes`, then re-ran the k6 benchmarks to verify 100% check success (`120 held`, `0 available`, `0 oversold`) across both single and multi-instance (`backend` + `backend2`) deployments.
