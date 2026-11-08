using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Domain;
using SchoolAdmission.Api.Middleware;

namespace SchoolAdmission.Api.Services;

public class HoldService
{
    private readonly AppDbContext _db;
    private readonly IdGenerator _idGen;
    private readonly IOptions<HoldOptions> _holdOptions;
    private readonly IOptions<DatabaseOptions> _dbOptions;

    public HoldService(
        AppDbContext db,
        IdGenerator idGen,
        IOptions<HoldOptions> holdOptions,
        IOptions<DatabaseOptions> dbOptions)
    {
        _db = db;
        _idGen = idGen;
        _holdOptions = holdOptions;
        _dbOptions = dbOptions;
    }

    private int HoldDurationSeconds => _holdOptions.Value.HoldDurationSeconds;
    private int LockTimeoutSeconds => _dbOptions.Value.LockTimeoutSeconds;

    private NpgsqlConnection GetConnection()
        => (NpgsqlConnection)_db.Database.GetDbConnection();

    public async Task<CreateHoldResponse> CreateHoldAsync(string classId, string studentId, CancellationToken ct = default)
    {
        var holdId = _idGen.NewHoldId();
        var duration = HoldDurationSeconds;

        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            // Fast path: unlocked pre-check to avoid lock contention when class is full
            await using (var fastCmd = new NpgsqlCommand(@"
                SELECT
                    c.total_seats,
                    COALESCE(SUM(CASE WHEN h.status IN ('HELD','CONFIRMED') AND (h.status='CONFIRMED' OR h.expires_at > now()) THEN 1 ELSE 0 END), 0)::int
                FROM classes c
                LEFT JOIN holds h ON h.class_id = c.id
                WHERE c.id = @classId
                GROUP BY c.total_seats", conn))
            {
                fastCmd.Parameters.AddWithValue("classId", classId);
                await using var fastReader = await fastCmd.ExecuteReaderAsync(ct);
                if (await fastReader.ReadAsync(ct))
                {
                    var totalSeats = fastReader.GetInt32(0);
                    var currentCount = fastReader.GetInt32(1);
                    if (currentCount >= totalSeats)
                        throw AppException.NoSeatsAvailable();
                }
            }

            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // Set lock timeout
                await using (var lockTimeoutCmd = new NpgsqlCommand($"SET LOCAL lock_timeout = '{LockTimeoutSeconds}s'", conn, tx))
                    await lockTimeoutCmd.ExecuteNonQueryAsync(ct);

                // Step 2: Lock the class row
                int totalSeats;
                await using (var lockCmd = new NpgsqlCommand(
                    "SELECT total_seats FROM classes WHERE id = @classId FOR UPDATE", conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("classId", classId);
                    var result = await lockCmd.ExecuteScalarAsync(ct);
                    if (result is null)
                        throw AppException.NotFound($"Class '{classId}' not found.");
                    totalSeats = (int)result;
                }

                // Step 3: get DB clock
                DateTime now;
                await using (var clockCmd = new NpgsqlCommand("SELECT clock_timestamp()", conn, tx))
                    now = (DateTime)(await clockCmd.ExecuteScalarAsync(ct))!;

                // Step 4: expire old HELD holds
                await using (var expireCmd = new NpgsqlCommand(
                    "UPDATE holds SET status='EXPIRED' WHERE class_id=@classId AND status='HELD' AND expires_at <= @now",
                    conn, tx))
                {
                    expireCmd.Parameters.AddWithValue("classId", classId);
                    expireCmd.Parameters.AddWithValue("now", now);
                    await expireCmd.ExecuteNonQueryAsync(ct);
                }

                // Step 5: check duplicate
                await using (var dupCmd = new NpgsqlCommand(
                    "SELECT 1 FROM holds WHERE class_id=@classId AND student_id=@studentId AND status IN ('HELD','CONFIRMED') LIMIT 1",
                    conn, tx))
                {
                    dupCmd.Parameters.AddWithValue("classId", classId);
                    dupCmd.Parameters.AddWithValue("studentId", studentId);
                    var dup = await dupCmd.ExecuteScalarAsync(ct);
                    if (dup is not null)
                        throw AppException.AlreadyApplied();
                }

                // Step 6: count seats
                int activeCount;
                await using (var countCmd = new NpgsqlCommand(
                    "SELECT COUNT(*)::int FROM holds WHERE class_id=@classId AND status IN ('HELD','CONFIRMED')",
                    conn, tx))
                {
                    countCmd.Parameters.AddWithValue("classId", classId);
                    var countResult = await countCmd.ExecuteScalarAsync(ct);
                    activeCount = Convert.ToInt32(countResult);
                }
                if (activeCount >= totalSeats)
                    throw AppException.NoSeatsAvailable();

                // Step 7: insert hold
                var expiresAt = now.AddSeconds(duration);
                await using (var insertCmd = new NpgsqlCommand(@"
                    INSERT INTO holds (id, class_id, student_id, status, expires_at, created_at)
                    VALUES (@id, @classId, @studentId, 'HELD', @expiresAt, @now)",
                    conn, tx))
                {
                    insertCmd.Parameters.AddWithValue("id", holdId);
                    insertCmd.Parameters.AddWithValue("classId", classId);
                    insertCmd.Parameters.AddWithValue("studentId", studentId);
                    insertCmd.Parameters.AddWithValue("expiresAt", expiresAt);
                    insertCmd.Parameters.AddWithValue("now", now);
                    await insertCmd.ExecuteNonQueryAsync(ct);
                }

                // Durable commit (not cancelled by client disconnect)
                await tx.CommitAsync(CancellationToken.None);
                return new CreateHoldResponse(holdId, classId, studentId, HoldStatus.Held,
                    DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc),
                    DateTime.SpecifyKind(now, DateTimeKind.Utc));
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        catch (NpgsqlException ex) when (ex.Message.Contains("lock timeout") || ex.Message.Contains("canceling statement"))
        {
            throw AppException.TemporarilyBusy();
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<HoldResponse> GetHoldAsync(string holdId, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(@"
                SELECT id, class_id, status, expires_at, clock_timestamp() as server_time
                FROM holds WHERE id = @holdId", conn);
            cmd.Parameters.AddWithValue("holdId", holdId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw AppException.NotFound($"Hold '{holdId}' not found.");

            var id = reader.GetString(0);
            var classId = reader.GetString(1);
            var status = reader.GetString(2);
            var expiresAt = DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc);
            var serverTime = DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc);

            // Derive EXPIRED status
            if (status == HoldStatus.Held && expiresAt <= serverTime)
                status = HoldStatus.Expired;

            return new HoldResponse(id, classId, status, expiresAt, serverTime);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<ConfirmHoldResponse> ConfirmHoldAsync(string holdId, string studentId, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using (var lockTimeoutCmd = new NpgsqlCommand($"SET LOCAL lock_timeout = '{LockTimeoutSeconds}s'", conn, tx))
                    await lockTimeoutCmd.ExecuteNonQueryAsync(ct);

                // Get hold first to find classId
                string classId;
                string currentStatus;
                string ownerId;
                DateTime expiresAt;
                await using (var holdCmd = new NpgsqlCommand(
                    "SELECT class_id, status, student_id, expires_at FROM holds WHERE id = @holdId",
                    conn, tx))
                {
                    holdCmd.Parameters.AddWithValue("holdId", holdId);
                    await using var reader = await holdCmd.ExecuteReaderAsync(ct);
                    if (!await reader.ReadAsync(ct))
                        throw AppException.NotFound($"Hold '{holdId}' not found.");
                    classId = reader.GetString(0);
                    currentStatus = reader.GetString(1);
                    ownerId = reader.GetString(2);
                    expiresAt = reader.GetDateTime(3);
                }

                // Check owner
                if (ownerId != studentId)
                    throw AppException.NotYourHold();

                // Lock class row
                await using (var lockCmd = new NpgsqlCommand(
                    "SELECT 1 FROM classes WHERE id = @classId FOR UPDATE", conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("classId", classId);
                    await lockCmd.ExecuteScalarAsync(ct);
                }

                // DB clock
                DateTime now;
                await using (var clockCmd = new NpgsqlCommand("SELECT clock_timestamp()", conn, tx))
                    now = (DateTime)(await clockCmd.ExecuteScalarAsync(ct))!;

                // Re-read hold inside lock
                await using (var rereadCmd = new NpgsqlCommand(
                    "SELECT status, expires_at FROM holds WHERE id = @holdId FOR UPDATE", conn, tx))
                {
                    rereadCmd.Parameters.AddWithValue("holdId", holdId);
                    await using var r = await rereadCmd.ExecuteReaderAsync(ct);
                    await r.ReadAsync(ct);
                    currentStatus = r.GetString(0);
                    expiresAt = DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc);
                }

                // Business logic checks
                if (currentStatus == HoldStatus.Confirmed)
                {
                    await tx.CommitAsync(CancellationToken.None);
                    return new ConfirmHoldResponse(holdId, HoldStatus.Confirmed);
                }
                if (currentStatus == HoldStatus.Cancelled)
                    throw AppException.HoldCancelled();
                if (currentStatus == HoldStatus.Expired ||
                    (currentStatus == HoldStatus.Held && expiresAt <= DateTime.SpecifyKind(now, DateTimeKind.Utc)))
                    throw AppException.HoldExpired();

                // Confirm
                await using (var confirmCmd = new NpgsqlCommand(@"
                    UPDATE holds SET status='CONFIRMED', confirmed_at=@now WHERE id=@holdId",
                    conn, tx))
                {
                    confirmCmd.Parameters.AddWithValue("now", now);
                    confirmCmd.Parameters.AddWithValue("holdId", holdId);
                    await confirmCmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(CancellationToken.None);
                return new ConfirmHoldResponse(holdId, HoldStatus.Confirmed);
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        catch (NpgsqlException ex) when (ex.Message.Contains("lock timeout") || ex.Message.Contains("canceling statement"))
        {
            throw AppException.TemporarilyBusy();
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task CancelHoldAsync(string holdId, string studentId, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using (var lockTimeoutCmd = new NpgsqlCommand($"SET LOCAL lock_timeout = '{LockTimeoutSeconds}s'", conn, tx))
                    await lockTimeoutCmd.ExecuteNonQueryAsync(ct);

                // Get hold
                string classId;
                string currentStatus;
                string ownerId;
                await using (var holdCmd = new NpgsqlCommand(
                    "SELECT class_id, status, student_id FROM holds WHERE id = @holdId", conn, tx))
                {
                    holdCmd.Parameters.AddWithValue("holdId", holdId);
                    await using var reader = await holdCmd.ExecuteReaderAsync(ct);
                    if (!await reader.ReadAsync(ct))
                        throw AppException.NotFound($"Hold '{holdId}' not found.");
                    classId = reader.GetString(0);
                    currentStatus = reader.GetString(1);
                    ownerId = reader.GetString(2);
                }

                // Check owner
                if (ownerId != studentId)
                    throw AppException.NotYourHold();

                // Already done: idempotent
                if (currentStatus == HoldStatus.Cancelled || currentStatus == HoldStatus.Expired)
                {
                    await tx.CommitAsync(CancellationToken.None);
                    return;
                }

                if (currentStatus == HoldStatus.Confirmed)
                    throw AppException.AlreadyConfirmed();

                // Lock class row
                await using (var lockCmd = new NpgsqlCommand(
                    "SELECT 1 FROM classes WHERE id = @classId FOR UPDATE", conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("classId", classId);
                    await lockCmd.ExecuteScalarAsync(ct);
                }

                // DB clock
                DateTime now;
                await using (var clockCmd = new NpgsqlCommand("SELECT clock_timestamp()", conn, tx))
                    now = (DateTime)(await clockCmd.ExecuteScalarAsync(ct))!;

                // Cancel
                await using (var cancelCmd = new NpgsqlCommand(@"
                    UPDATE holds SET status='CANCELLED', cancelled_at=@now WHERE id=@holdId AND status='HELD'",
                    conn, tx))
                {
                    cancelCmd.Parameters.AddWithValue("now", now);
                    cancelCmd.Parameters.AddWithValue("holdId", holdId);
                    await cancelCmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(CancellationToken.None);
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        catch (NpgsqlException ex) when (ex.Message.Contains("lock timeout") || ex.Message.Contains("canceling statement"))
        {
            throw AppException.TemporarilyBusy();
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<List<MyHoldResponse>> GetMyHoldsAsync(string studentId, CancellationToken ct = default)
    {
        var conn = GetConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(@"
                SELECT h.id, h.class_id, c.name, h.status, h.expires_at, now() as server_now
                FROM holds h
                JOIN classes c ON c.id = h.class_id
                WHERE h.student_id = @studentId
                ORDER BY h.created_at DESC", conn);
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

                // Derive EXPIRED
                if (status == HoldStatus.Held && expiresAt <= serverNow)
                    status = HoldStatus.Expired;

                results.Add(new MyHoldResponse(id, classId, className, status, expiresAt));
            }
            return results;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
