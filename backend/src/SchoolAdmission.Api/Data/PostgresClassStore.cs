using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data.Abstractions;
using SchoolAdmission.Api.Data.Sql;

namespace SchoolAdmission.Api.Data;

public class PostgresClassStore : IClassStore
{
    private readonly AppDbContext _db;

    public PostgresClassStore(AppDbContext db)
    {
        _db = db;
    }

    private NpgsqlConnection GetConnection() => (NpgsqlConnection)_db.Database.GetDbConnection();

    public async Task<ClassResponse> CreateClassAsync(string id, string name, int totalSeats, DateTime createdAt, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(ClassQueries.InsertClass, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("totalSeats", totalSeats);
            cmd.Parameters.AddWithValue("createdAt", createdAt);
            await cmd.ExecuteNonQueryAsync(ct);

            return new ClassResponse(id, name, totalSeats);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<List<ClassDetailResponse>> GetAllClassesAsync(CancellationToken ct = default)
    {
        var result = new List<ClassDetailResponse>();
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(ClassQueries.SelectAllWithSeatCounts, conn);
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
            return result;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<ClassDetailResponse?> GetClassByIdAsync(string classId, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(ClassQueries.SelectByIdWithSeatCounts, conn);
            cmd.Parameters.AddWithValue("classId", classId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

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
