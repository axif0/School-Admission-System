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
            COALESCE(SUM(CASE WHEN h.status = 'HELD' AND h.expires_at > now() THEN 1 ELSE 0 END), 0)::int AS held_seats,
            COALESCE(SUM(CASE WHEN h.status = 'CONFIRMED' THEN 1 ELSE 0 END), 0)::int AS confirmed_seats
        FROM classes c
        LEFT JOIN holds h ON h.class_id = c.id
        GROUP BY c.id, c.name, c.total_seats, c.created_at
        ORDER BY c.created_at ASC";

    public const string SelectByIdWithSeatCounts = @"
        SELECT
            c.id,
            c.name,
            c.total_seats,
            COALESCE(SUM(CASE WHEN h.status = 'HELD' AND h.expires_at > now() THEN 1 ELSE 0 END), 0)::int AS held_seats,
            COALESCE(SUM(CASE WHEN h.status = 'CONFIRMED' THEN 1 ELSE 0 END), 0)::int AS confirmed_seats
        FROM classes c
        LEFT JOIN holds h ON h.class_id = c.id
        WHERE c.id = @classId
        GROUP BY c.id, c.name, c.total_seats";
}
