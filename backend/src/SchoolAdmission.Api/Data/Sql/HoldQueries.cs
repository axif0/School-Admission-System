namespace SchoolAdmission.Api.Data.Sql;

public static class HoldQueries
{
    public const string FastPathSeatCheck = @"
        SELECT
            c.total_seats,
            COALESCE(SUM(CASE WHEN h.status IN ('HELD','CONFIRMED') AND (h.status='CONFIRMED' OR h.expires_at > now()) THEN 1 ELSE 0 END), 0)::int
        FROM classes c
        LEFT JOIN holds h ON h.class_id = c.id
        WHERE c.id = @classId
        GROUP BY c.total_seats";

    public const string SelectHoldById = @"
        SELECT id, class_id, status, expires_at, clock_timestamp() as server_time
        FROM holds WHERE id = @holdId";

    public const string SelectHoldsByStudent = @"
        SELECT h.id, h.class_id, c.name, h.status, h.expires_at, now() as server_now
        FROM holds h
        JOIN classes c ON c.id = h.class_id
        WHERE h.student_id = @studentId
        ORDER BY h.created_at DESC";

    public const string LockClassRow = @"
        SELECT total_seats FROM classes WHERE id = @classId FOR UPDATE";

    public const string SelectDbClock = @"
        SELECT clock_timestamp()";

    public const string ExpireStaleHoldsForClass = @"
        UPDATE holds SET status='EXPIRED'
        WHERE class_id=@classId AND status='HELD' AND expires_at <= @now";

    public const string CheckActiveHoldForStudent = @"
        SELECT 1 FROM holds
        WHERE class_id=@classId AND student_id=@studentId AND status IN ('HELD','CONFIRMED')
        LIMIT 1";

    public const string CountActiveSeatsForClass = @"
        SELECT COUNT(*)::int FROM holds
        WHERE class_id=@classId AND status IN ('HELD','CONFIRMED')";

    public const string InsertHold = @"
        INSERT INTO holds (id, class_id, student_id, status, expires_at, created_at)
        VALUES (@id, @classId, @studentId, 'HELD', @expiresAt, @now)";

    public const string SelectHoldForOwnershipCheck = @"
        SELECT class_id, status, student_id, expires_at, created_at
        FROM holds WHERE id = @holdId";

    public const string LockHoldRow = @"
        SELECT status, expires_at
        FROM holds WHERE id = @holdId FOR UPDATE";

    public const string ConfirmHold = @"
        UPDATE holds SET status='CONFIRMED', confirmed_at=@now
        WHERE id=@holdId";

    public const string CancelHold = @"
        UPDATE holds SET status='CANCELLED', cancelled_at=@now
        WHERE id=@holdId AND status='HELD'";

    public const string SweeperExpireHolds = @"
        UPDATE holds SET status='EXPIRED'
        WHERE status='HELD' AND expires_at <= clock_timestamp()";
}
