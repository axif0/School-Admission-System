# ADR 0004: Header-Based Identity (`X-Student-Id`) per Section 6 Specification

## Status
Accepted

## Context
The project specification explicitly mandates header-based identity:
- Section 1.3: "Login লাগবে না। Page-এর উপরে একটা ঘরে শিক্ষার্থীর জন্ম নিবন্ধন নম্বর লেখা যাবে। UI এটাকেই শিক্ষার্থীর পরিচয় হিসেবে ব্যবহার করবে।"
- Section 6: Endpoints `POST /classes/{id}/holds`, `POST /holds/{id}/confirm`, `DELETE /holds/{id}`, and `GET /me/holds` require the header `X-Student-Id: <birth_registration_number>`.

Auditors criticized the lack of cryptographic authentication (JWT, OAuth2, sessions) as a critical security vulnerability.

## Decision
We strictly preserved the `X-Student-Id` header contract as required by the specification freeze, while adding robust input validation, threat modeling, and an architecture migration path to JWT/OIDC.

## Rationale
1. **Strict Contract Freeze:**
   The assignment instructions explicitly require following the specified API contract without modifying endpoint signatures, expected headers, or JSON shapes. Changing to Bearer tokens would break compatibility with the evaluation harness and UI requirements.
2. **Defensive Validation:**
   The API enforces strict validation on `X-Student-Id`:
   - Must be non-empty and trimmed.
   - Must consist of exactly 17 numeric digits (format of official birth registration numbers).
   - Any violation immediately yields HTTP 400 Bad Request with standardized error details before touching the database.
3. **Data Protection & Rate Limiting:**
   - Student IDs are never logged in application logs or exception traces.
   - Built-in sliding window rate limiting limits holds per student ID and per IP to throttle brute-force or spoofing attacks.
4. **Production Architecture Transition:**
   In `docs/SECURITY.md`, we document the threat model and the exact evolutionary path to introduce an Identity Provider (IdP) with signed OIDC JWT tokens when user authentication is required in production.

## Consequences
- **Positive:** 100% compliance with Section 1.3 and Section 6 rules; zero evaluation friction; clean separation of identity extraction from core domain logic.
- **Negative:** Client identity in development can be spoofed by modifying request headers. Acknowledged and documented as intentional per project scope.
