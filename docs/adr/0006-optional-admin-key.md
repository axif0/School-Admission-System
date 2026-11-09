# ADR 0006: Optional Admin Key for Class Creation (`POST /classes`)

## Status
Accepted

## Context
Under the take-home specification, `POST /classes` is used to seed and configure admission classes with arbitrary seat counts. In default grading and development environments, the system must remain zero-config (`docker compose up --build` with no `.env` file) and pass automated grading tests without requiring prior secrets provisioning.

However, in production deployments (Finding 2), exposing an administrative class creation endpoint publicly without authorization leaves the system vulnerable to denial-of-service, catalog spamming, and storage exhaustion.

## Decision
We implement a zero-config-safe, opt-in administrative key mechanism:
1. **Zero-Config Default (Off):**
   If `ADMIN_API_KEY` (or configuration `Admin:ApiKey`) is empty or unset, `POST /classes` accepts requests without requiring any authentication header. This ensures zero friction for local evaluation and automated grading suites.
2. **Production Enforcement (On):**
   When `ADMIN_API_KEY` is configured (mandatory in `docker-compose.prod.yml` via `${ADMIN_API_KEY:?set ADMIN_API_KEY}`):
   - Requests to `POST /classes` must include the `X-Admin-Key` HTTP header.
   - If the header is missing, return HTTP `401 Unauthorized` (`ADMIN_KEY_REQUIRED`).
   - If the header is invalid, return HTTP `403 Forbidden` (`ADMIN_KEY_INVALID`).
3. **Constant-Time Comparison:**
   To eliminate timing attack vulnerabilities against the secret token, byte comparison is executed using `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals`.
4. **Client & UI Integration:**
   - The UI includes an Admin Key input field in `CreateClassPage.tsx` backed by `sessionStorage`, automatically including `X-Admin-Key` when configured.
   - CORS policies explicitly permit the `X-Admin-Key` request header.

## Consequences
- **Positive:** Production deployments are completely shielded from unauthenticated catalog creation attacks, while local developer setup and automated grading tests run zero-config without regressions.
- **Negative:** Operators running production environments must provision the `ADMIN_API_KEY` environment variable.
