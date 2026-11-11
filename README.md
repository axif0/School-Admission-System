# School Admission System (Seat-Hold System)

A production-ready full-stack admission system designed to handle heavy concurrency and flash traffic during school admission windows. Built with **.NET 8 (C#)**, **PostgreSQL 16**, and **React 18 (TypeScript + Vite)**.

<img width="1151" height="733" alt="image" src="https://github.com/user-attachments/assets/14cadbce-4e80-41d7-939b-e2b8db5b393e" />

---
## How Simultaneous Seat Requests are Handled

When large group of users, request seats simultaneously one at a time,  PostgreSQL's SELECT FOR UPDATE row-level locking serializes allocation transactions. Like if we got 10k requests, for the row lock the transactions will be serialized and first succesful req will be hold for seat alocation. 

```
1000 Write Requests
        |
        v
   ClassWriteGate
        |
        +---- 5 Requests ----> Database
        |
        +---- 995 Waiting
```

### SQL Example

```sql
BEGIN;

SELECT total_seats
FROM classes
WHERE id = 10
FOR UPDATE;

-- Check active holds
-- Check available seats
-- Insert new hold

COMMIT;

```
Here, when transaction started the class row was locked. Available seat was checked. Then new hold was inserted. After that transaction was committed and lock was released.

Also, if the frontend or backend is turn off using docker composer down, it will not affect the postgresql database data as it is in persistent volume.

---

## Running with Docker

### 1. Start the Application
Run the following command in the project root to build and launch all containers:
```bash
docker compose up -d --build
```

### 2. Service URLs
- **Frontend UI:** [http://localhost:3000](http://localhost:3000)
- **Backend API:** [http://localhost:8080](http://localhost:8080)
- **Health Check:** [http://localhost:8080/health/ready](http://localhost:8080/health/ready)

### 3. Check Container Status & Logs
```bash
# Check status of running containers
docker compose ps

# View live container logs
docker compose logs -f
```

### 4. Stop the Application
```bash
docker compose down
```
*(To also remove persistent database volumes: `docker compose down -v`)*

---

## Running Tests

### Via Docker (Recommended)
```bash
# Run backend unit & integration tests
docker compose run --rm backend-tests

# Run frontend tests
docker compose run --rm frontend-tests

# Run API contract smoke test (while services are running)
./scripts/contract-smoke.sh
```

### Locally on Host Machine
* **Backend:**
  ```bash
  dotnet test backend/SchoolAdmission.sln
  ```
* **Frontend:**
  ```bash
  cd frontend
  npm install
  npm test
  ```




