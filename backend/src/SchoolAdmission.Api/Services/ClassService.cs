using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Domain;
using SchoolAdmission.Api.Middleware;

namespace SchoolAdmission.Api.Services;

public class ClassService
{
    private readonly AppDbContext _db;
    private readonly IdGenerator _idGen;

    public ClassService(AppDbContext db, IdGenerator idGen)
    {
        _db = db;
        _idGen = idGen;
    }

    public async Task<ClassResponse> CreateAsync(CreateClassRequest req, CancellationToken ct = default)
    {
        // Validation
        var name = req.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 100)
            throw AppException.ValidationError("'name' must be between 1 and 100 characters.");
        if (req.TotalSeats < 1 || req.TotalSeats > 100_000)
            throw AppException.ValidationError("'totalSeats' must be between 1 and 100000.");

        var cls = new Class
        {
            Id = _idGen.NewClassId(),
            Name = name,
            TotalSeats = req.TotalSeats,
            CreatedAt = DateTime.UtcNow
        };

        _db.Classes.Add(cls);
        await _db.SaveChangesAsync(ct);

        return new ClassResponse(cls.Id, cls.Name, cls.TotalSeats);
    }

    public async Task<List<ClassDetailResponse>> GetAllAsync(CancellationToken ct = default)
    {
        // Single grouped query for all classes with seat counts
        var sql = @"
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

        var result = new List<ClassDetailResponse>();
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                var total = reader.GetInt32(2);
                var held = reader.GetInt32(3);
                var confirmed = reader.GetInt32(4);
                result.Add(new ClassDetailResponse(id, name, total, held, confirmed, total - held - confirmed));
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
        return result;
    }

    public async Task<ClassDetailResponse> GetByIdAsync(string classId, CancellationToken ct = default)
    {
        var sql = @"
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

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("classId", classId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw AppException.NotFound($"Class '{classId}' not found.");

            var id = reader.GetString(0);
            var name = reader.GetString(1);
            var total = reader.GetInt32(2);
            var held = reader.GetInt32(3);
            var confirmed = reader.GetInt32(4);
            return new ClassDetailResponse(id, name, total, held, confirmed, total - held - confirmed);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
