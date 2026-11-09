# Baseline Verification Results (`docs/BASELINE.md`)

> **Date:** November 9, 2026 (12:43 UTC+6)  
> **Environment:** Linux 6.8.0-48-generic x86_64, .NET SDK 8.0.427, Node.js 20.x, Docker 27.x, PostgreSQL 16 Alpine  
> **Verification Gate:** WP0 Baseline & Claims Audit (prompt3.md)

---

## 1. Backend Test Suite

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
dotnet test backend/SchoolAdmission.sln --logger "console;verbosity=normal"
```

### Outcome: **PASSED (41 of 41 tests)**
```text
Test run for /media/asif/Mahbub12/School-Admission-System/backend/tests/SchoolAdmission.Tests/bin/Debug/net8.0/SchoolAdmission.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.01] xUnit.net VSTest Adapter v2.8.2+699d445a1a (64-bit .NET 8.0.33)
[xUnit.net 00:00:00.38]   Discovering: SchoolAdmission.Tests
[xUnit.net 00:00:00.63]   Discovered:  SchoolAdmission.Tests
[xUnit.net 00:00:00.65]   Starting:    SchoolAdmission.Tests
  Passed SchoolAdmission.Tests.HoldStateMachineTests.DeriveStatus_DerivesExpired_WhenHeldAndPastExpiry [41 ms]
  Passed SchoolAdmission.Tests.HoldStateMachineTests.ValidateConfirmation_RejectsCancelledHold [10 ms]
  Passed SchoolAdmission.Tests.HoldStateMachineTests.IsTerminal_ReturnsTrueForTerminalStates [< 1 ms]
  Passed SchoolAdmission.Tests.HoldStateMachineTests.ValidateCancellation_RejectsConfirmedHold [< 1 ms]
  Passed SchoolAdmission.Tests.OptionsTests.HoldOptions_InvalidDuration_FailsValidation(invalidDuration: -10) [87 ms]
  ...
  Passed SchoolAdmission.Tests.HoldLifecycleTests.ExpiryFreesSeat_AutomaticallyWithoutApiCall [3 s]
  Passed SchoolAdmission.Tests.ConcurrencyTests.NoOverselling_UnderHeavyParallelLoad_NeverExceedsTotalSeats [1 s]
  Passed SchoolAdmission.Tests.HoldLifecycleTests.CancelRules_HeldFreed_ConfirmedRejected_IdempotentOnCancelled [179 ms]
  Passed SchoolAdmission.Tests.RateLimiterTests.RateLimiter_WhenEnabled_RejectsExceedingRequestsWith429AndRetryAfter [120 ms]
  Passed SchoolAdmission.Tests.ClassQueryCoalescerTests.ExecuteCoalescedAsync_CoalescesInFlightQueries_BetweenConcurrentCallers [10 ms]

Total tests: 41
     Passed: 41
 Total time: 16.8182 Seconds
```

---

## 2. Frontend Test Suite

```bash
cd frontend && npm test -- --run
```

### Outcome: **PASSED (2 of 2 tests)**
```text
 RUN  v1.6.1 /media/asif/Mahbub12/School-Admission-System/frontend

 · src/test/frontend.test.tsx (2)
   · Frontend Unit Tests (2)
     · Validates 17-digit student id and renders error state
     · Maps error codes to friendly human language (Bengali & English)
 ✓ src/test/frontend.test.tsx (2)
   ✓ Frontend Unit Tests (2)
     ✓ Validates 17-digit student id and renders error state
     ✓ Maps error codes to friendly human language (Bengali & English)

 Test Files  1 passed (1)
      Tests  2 passed (2)
   Start at  12:11:18
   Duration  22.27s
```

---

## 3. Strict API Contract Smoke Suite

```bash
bash scripts/contract-smoke.sh
```

### Outcome: **PASSED (25 of 25 assertions)**
```text
==========================================================
 Running Section 6 Strict API Contract Smoke Tests
 Target: http://localhost:8080
==========================================================
1. POST /classes (Create Class)
  [PASS] Status is 201
  [PASS] Class ID has prefix 'c_'
  [PASS] Class name matches
  [PASS] Total seats is 2
2. GET /classes (List Classes)
  [PASS] Status is 200
  [PASS] Created class present in list
3. GET /classes/{classId} (Get Class Details)
  [PASS] Status is 200
  [PASS] Class ID matches
  [PASS] Available seats initially 2
4. POST /classes/{classId}/holds (Create Hold)
  [PASS] Status is 201
  [PASS] Hold ID has prefix 'h_'
  [PASS] Status is HELD
  [PASS] Student ID matches
  [PASS] ExpiresAt ends with Z
5. GET /holds/{holdId} (Get Hold)
  [PASS] Status is 200
  [PASS] Hold ID matches
  [PASS] Status is HELD
6. POST /holds/{holdId}/confirm (Confirm Hold)
  [PASS] Status is 200
  [PASS] Status is CONFIRMED
7. DELETE /holds/{holdId} (Cancel Active Hold)
  [PASS] Delete status is 204
8. GET /me/holds (My Holds)
  [PASS] Status is 200
  [PASS] My hold status is CONFIRMED
9. Overbooking test -> 409 Conflict
  [PASS] Overbooked hold returns 409
  [PASS] Error code is NO_SEATS_AVAILABLE
  [PASS] Error contains traceId
==========================================================
 ALL CONTRACT SMOKE TESTS PASSED (25 assertions)
==========================================================
```

---

## 4. Multi-Process Concurrency Hammer

```bash
bash scripts/hammer.sh
```

### Outcome: **PASSED (Exact concurrency invariant held)**
```text
=== School Admission Multi-Process Concurrency Hammer ===
Checking backend 1 at http://localhost:8080/health...
Checking backend 2 at http://localhost:8081/health...
Creating test class with 15 seats via Backend 1...
Created class: c_392825dnw7sv with 15 seats.
Firing 100 simultaneous hold requests across both backends...
Results:
  Success (201 Created): 15
  Conflict (409 No Seats): 85
  Other: 0
Class status from Backend 2: {"id":"c_392825dnw7sv","name":"Multi-Process Concurrency Test","totalSeats":15,"heldSeats":15,"confirmedSeats":0,"availableSeats":0}
✅ SUCCESS: Invariant preserved! Exactly 15 holds succeeded across both backends.
```

---

## 5. Docker Stack State

```text
CONTAINER ID   IMAGE                              COMMAND                  STATUS                    PORTS
4a83ad52da53   school-admission-system-frontend   "/docker-entrypoint.…"   Up (healthy)              0.0.0.0:3000->8080/tcp
7228d17f344b   school-admission-system-backend    "dotnet SchoolAdmiss…"   Up (healthy)              0.0.0.0:8080->8080/tcp
09951e7d70c0   postgres:16-alpine                 "docker-entrypoint.s…"   Up (healthy)              127.0.0.1:5432->5432/tcp
86f120d34b9b   school-admission-system-backend2   "dotnet SchoolAdmiss…"   Up (healthy)              0.0.0.0:8081->8080/tcp
```
