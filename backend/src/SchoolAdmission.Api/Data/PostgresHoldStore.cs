using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data.Abstractions;
using SchoolAdmission.Api.Data.Sql;
using SchoolAdmission.Api.Domain;

namespace SchoolAdmission.Api.Data;

public class PostgresHoldStore : IHoldStore
{
    private readonly AppDbContext _db;

    public PostgresHoldStore(AppDbContext db)
    {
        _db = db;
    }

    private NpgsqlConnection GetConnection() => (NpgsqlConnection)_db.Database.GetDbConnection();

    public async Task<IHoldSession> OpenSessionAsync(string classId, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        return new PostgresHoldSession(conn, classId);
    }

    public async Task<FastPathSeatCheckResult> CheckFastPathSeatsAsync(string classId, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(HoldQueries.FastPathSeatCheck, conn);
            cmd.Parameters.AddWithValue("classId", classId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var total = reader.GetInt32(0);
                var active = reader.GetInt32(1);
                return new FastPathSeatCheckResult(true, total, active);
            }
            return new FastPathSeatCheckResult(false, 0, 0);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<HoldRecord?> GetHoldByIdAsync(string holdId, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(HoldQueries.SelectHoldById, conn);
            cmd.Parameters.AddWithValue("holdId", holdId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            var id = reader.GetString(0);
            var classId = reader.GetString(1);
            var status = reader.GetString(2);
            var expiresAt = DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc);
            var serverTime = DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc);

            return new HoldRecord(id, classId, status, expiresAt, serverTime);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<HoldOwnershipRecord?> GetHoldForOwnershipCheckAsync(string holdId, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(HoldQueries.SelectHoldForOwnershipCheck, conn);
            cmd.Parameters.AddWithValue("holdId", holdId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return new HoldOwnershipRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc));
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<List<MyHoldResponse>> GetHoldsForStudentAsync(string studentId, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(HoldQueries.SelectHoldsByStudent, conn);
            cmd.Parameters.AddWithValue("studentId", studentId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var results = new List<MyHoldResponse>();
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetString(0);
                var classId = reader.GetString(1);
                var className = reader.GetString(2);
                var status = reader.GetString(3);
                var expiresAt = DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc);
                var serverNow = DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc);

                var derivedStatus = HoldStateMachine.DeriveStatus(status, expiresAt, serverNow);
                results.Add(new MyHoldResponse(id, classId, className, derivedStatus, expiresAt));
            }
            return results;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<IHoldTransaction> BeginHoldTransactionAsync(int lockTimeoutSeconds, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        var tx = await conn.BeginTransactionAsync(ct);
        var holdTx = new PostgresHoldTransaction(conn, tx);
        await holdTx.SetLockTimeoutAsync(lockTimeoutSeconds, ct);
        return holdTx;
    }
}

public class PostgresHoldTransaction : IHoldTransaction
{
    private readonly NpgsqlConnection _conn;
    private readonly NpgsqlTransaction _tx;
    private readonly bool _ownsConnection;
    private bool _isDisposed;

    public PostgresHoldTransaction(NpgsqlConnection conn, NpgsqlTransaction tx, bool ownsConnection = true)
    {
        _conn = conn;
        _tx = tx;
        _ownsConnection = ownsConnection;
    }

    public async Task SetLockTimeoutAsync(int timeoutSeconds, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand($"SET LOCAL lock_timeout = '{timeoutSeconds}s'", _conn, _tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int?> LockClassAsync(string classId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(HoldQueries.LockClassRow, _conn, _tx);
        cmd.Parameters.AddWithValue("classId", classId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null ? null : (int)result;
    }

    public async Task<DateTime> GetDbClockAsync(CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(HoldQueries.SelectDbClock, _conn, _tx);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (DateTime)result!;
    }

    public async Task<int> ExpireStaleHoldsAsync(string classId, DateTime now, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(HoldQueries.ExpireStaleHoldsForClass, _conn, _tx);
        cmd.Parameters.AddWithValue("classId", classId);
        cmd.Parameters.AddWithValue("now", now);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> HasActiveHoldForStudentAsync(string classId, string studentId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(HoldQueries.CheckActiveHoldForStudent, _conn, _tx);
        cmd.Parameters.AddWithValue("classId", classId);
        cmd.Parameters.AddWithValue("studentId", studentId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    public async Task<int> CountActiveSeatsAsync(string classId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(HoldQueries.CountActiveSeatsForClass, _conn, _tx);
        cmd.Parameters.AddWithValue("classId", classId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task InsertHoldAsync(string id, string classId, string studentId, DateTime expiresAt, DateTime now, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(HoldQueries.InsertHold, _conn, _tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("classId", classId);
        cmd.Parameters.AddWithValue("studentId", studentId);
        cmd.Parameters.AddWithValue("expiresAt", expiresAt);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<HoldLockState> LockHoldRowAsync(string holdId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(HoldQueries.LockHoldRow, _conn, _tx);
        cmd.Parameters.AddWithValue("holdId", holdId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var status = reader.GetString(0);
        var expiresAt = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
        return new HoldLockState(status, expiresAt);
    }

    public async Task ConfirmHoldAsync(string holdId, DateTime now, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(HoldQueries.ConfirmHold, _conn, _tx);
        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("holdId", holdId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CancelHoldAsync(string holdId, DateTime now, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(HoldQueries.CancelHold, _conn, _tx);
        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("holdId", holdId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _tx.CommitAsync(ct);
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await _tx.RollbackAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            await _tx.DisposeAsync();
            if (_ownsConnection)
            {
                await _conn.CloseAsync();
                await _conn.DisposeAsync();
            }
            _isDisposed = true;
        }
    }
}

public class PostgresHoldSession : IHoldSession
{
    private readonly NpgsqlConnection _conn;
    private readonly string _classId;
    private bool _isDisposed;

    public PostgresHoldSession(NpgsqlConnection conn, string classId)
    {
        _conn = conn;
        _classId = classId;
    }

    public async Task<FastPathSeatCheckResult> CheckCapacityAsync(CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(HoldQueries.FastPathSeatCheck, _conn);
        cmd.Parameters.AddWithValue("classId", _classId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var total = reader.GetInt32(0);
            var active = reader.GetInt32(1);
            return new FastPathSeatCheckResult(true, total, active);
        }
        return new FastPathSeatCheckResult(false, 0, 0);
    }

    public async Task<IHoldTransaction> BeginLockedAsync(int lockTimeoutSeconds, CancellationToken ct = default)
    {
        var tx = await _conn.BeginTransactionAsync(ct);
        var holdTx = new PostgresHoldTransaction(_conn, tx, ownsConnection: false);
        await holdTx.SetLockTimeoutAsync(lockTimeoutSeconds, ct);
        return holdTx;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            await _conn.CloseAsync();
            await _conn.DisposeAsync();
            _isDisposed = true;
        }
    }
}
