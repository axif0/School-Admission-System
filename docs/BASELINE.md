# Baseline Verification Results (`docs/BASELINE.md`)

> **Date:** November 9, 2026 (00:22 UTC+6)  
> **Environment:** Linux (x86_64), .NET SDK 8.0.33, Node.js 20, Docker 27.x, PostgreSQL 16 Alpine  
> **Verification Gate:** WP0 Baseline & Finding Verification

---

## 1. Backend Test Suite

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
dotnet test backend/SchoolAdmission.sln --logger "console;verbosity=normal"
```

### Outcome: **PASSED (9 of 9 tests)**
```text
Test run for /media/asif/Mahbub12/School-Admission-System/backend/tests/SchoolAdmission.Tests/bin/Debug/net8.0/SchoolAdmission.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (x64)

Passed SchoolAdmission.Tests.ConcurrencyTests.NoOverselling_UnderHeavyParallelLoad_NeverExceedsTotalSeats [2 s]
Passed SchoolAdmission.Tests.HoldLifecycleTests.ExpiryFreesSeat_AutomaticallyWithoutApiCall [3 s]
Passed SchoolAdmission.Tests.HoldLifecycleTests.CancelRules_HeldFreed_ConfirmedRejected_IdempotentOnCancelled [179 ms]
Passed SchoolAdmission.Tests.ValidationTests.NonExistentClassOrHold_Returns404NotFound [941 ms]
...
Total tests: 9
     Passed: 9
 Total time: 11.3146 Seconds
```

---

## 2. Frontend Test Suite

```bash
cd frontend && npm test -- --run
```

### Outcome: **PASSED (2 of 2 tests)**
```text
 RUN  v1.6.1 /media/asif/Mahbub12/School-Admission-System/frontend

 ✓ src/test/frontend.test.tsx (2)
   ✓ Frontend Unit Tests (2)
     ✓ Validates 17-digit student id and renders error state
     ✓ Maps error codes to friendly human language (Bengali & English)

 Test Files  1 passed (1)
      Tests  2 passed (2)
   Duration  13.74s
```

---

## 3. Multi-Process Concurrency Hammer

```bash
bash scripts/hammer.sh
```

### Outcome: **PASSED (Exact concurrency invariant held)**
```text
=== School Admission Multi-Process Concurrency Hammer ===
Checking backend 1 at http://localhost:8080/health...
Checking backend 2 at http://localhost:8081/health...
Creating test class with 15 seats via Backend 1...
Created class: c_h0atw1w7m61j with 15 seats.
Firing 100 simultaneous hold requests across both backends...
Results:
  Success (201 Created): 15
  Conflict (409 No Seats): 85
  Other: 0
Class status from Backend 2: {"id":"c_h0atw1w7m61j","name":"Multi-Process Concurrency Test","totalSeats":15,"heldSeats":15,"confirmedSeats":0,"availableSeats":0}
✅ SUCCESS: Invariant preserved! Exactly 15 holds succeeded across both backends.
```

---

## 4. Docker Compose Environment State

```text
NAME                        SERVICE    STATUS                    PORTS
school_admission_backend    backend    Up (healthy)              0.0.0.0:8080->8080/tcp
school_admission_backend2   backend2   Up (healthy)              0.0.0.0:8081->8080/tcp
school_admission_db         db         Up (healthy)              0.0.0.0:5432->5432/tcp
school_admission_frontend   frontend   Up                        0.0.0.0:3000->80/tcp
```
