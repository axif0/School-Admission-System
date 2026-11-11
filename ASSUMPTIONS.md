# Assumptions

The specification does not explicitly define a few business rules and UI behaviors. For those cases, the following assumptions were made to keep the system practical and user-friendly.

**Applying to Multiple Classes or Shifts:** A student can apply to and confirm admission in multiple classes or shifts simultaneously. Since the specification restricts a student to one active application per class and treats each shift as a separate class, allowing applications across different classes lets parents keep backup shift options.

**Reapplying After Cancellation or Expiry:** Students can apply again to the same class if their previous application is `CANCELLED` or `EXPIRED`. Only active (`HELD` or `CONFIRMED`) applications block a new hold, ensuring students do not lose their chance if they cancelled by mistake or missed the payment window.

**Cancelling Confirmed Applications:** Only applications in the `HELD` state can be cancelled (`DELETE /holds/{holdId}`), while attempting to cancel a `CONFIRMED` application returns `409 Conflict`. Since fee payment finalizes admission and the specification defines no refund or post-admission cancellation flow, confirmed seats remain locked.

**Duplicate Class Names:** Admins are allowed to create multiple classes with the same name. Because each class is uniquely identified by its generated ID (`c_...`), separate batches or sections can share a display name without causing conflicts.

**Existing Applications on the Class List:** When a student already has a `HELD` or `CONFIRMED` application in a class, the class list shows a "View Application" button instead of "Apply". This prevents avoidable `409 Conflict` errors from duplicate clicks and takes the user directly to their active application.

**Additional Practical Enhancements:** To make the application smoother to use, the UI includes a Bangla/English language switcher, visual seat-capacity progress bars, countdown warning colors, and real-time seat updates via Server-Sent Events (SSE) with a 4-second polling fallback. On the backend, `X-Student-Id` validation accepts any non-blank string up to 64 characters so automated API tests can use arbitrary test IDs while the UI enforces the 17-digit birth registration number.

**Multi-Process Concurrency Safety (Bonus):** To ensure `held + confirmed` never exceeds `totalSeats` even when two or more backend processes run on different ports against the same PostgreSQL database, seat allocation relies entirely on database-level locking rather than in-memory locks. During hold creation, the transaction locks the target class row with `SELECT total_seats FROM classes WHERE id = @classId FOR UPDATE`, evaluates expiry using PostgreSQL's `clock_timestamp()`, and enforces a partial unique index on `(class_id, student_id) WHERE status IN ('HELD', 'CONFIRMED')` so concurrent requests across processes are strictly serialized.
