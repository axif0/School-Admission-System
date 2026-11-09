namespace SchoolAdmission.Api.Data.Sql;

public static class ClassQueries
{
    public const string InsertClass = @"
        INSERT INTO classes (id, name, total_seats, created_at)
        VALUES (@id, @name, @totalSeats, @createdAt)";

    public const string SelectAllWithSeatCounts = @"
        SELECT
            c.id,
            c.name,
            c.total_seats,
            COALESCE(h.held_seats, 0) AS held_seats,
            COALESCE(h.confirmed_seats, 0) AS confirmed_seats
        FROM classes c
        LEFT JOIN (
            SELECT
                class_id,
                SUM(CASE WHEN status = 'HELD' AND expires_at > now() THEN 1 ELSE 0 END)::int AS held_seats,
                SUM(CASE WHEN status = 'CONFIRMED' THEN 1 ELSE 0 END)::int AS confirmed_seats
            FROM holds
            WHERE status IN ('HELD', 'CONFIRMED')
            GROUP BY class_id
        ) h ON h.class_id = c.id
        ORDER BY c.created_at ASC";

    public const string SelectByIdWithSeatCounts = @"
        SELECT
            c.id,
            c.name,
            c.total_seats,
            (SELECT COUNT(*)::int FROM holds h WHERE h.class_id = c.id AND h.status = 'HELD' AND h.expires_at > now()) AS held_seats,
            (SELECT COUNT(*)::int FROM holds h WHERE h.class_id = c.id AND h.status = 'CONFIRMED') AS confirmed_seats
        FROM classes c
        WHERE c.id = @classId";
}
