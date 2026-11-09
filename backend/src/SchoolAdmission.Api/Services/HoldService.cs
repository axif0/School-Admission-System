using Microsoft.Extensions.Options;
using Npgsql;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Data.Abstractions;
using SchoolAdmission.Api.Domain;
using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Api.Observability;

namespace SchoolAdmission.Api.Services;

public class HoldService : IHoldService
{
    private readonly IHoldStore _store;
    private readonly IdGenerator _idGen;
    private readonly IOptions<HoldOptions> _holdOptions;
    private readonly IOptions<DatabaseOptions> _dbOptions;
    private readonly ClassQueryCoalescer _coalescer;
    private readonly AppMetrics? _metrics;
    private readonly ClassWriteGate _writeGate;

    public HoldService(
        IHoldStore store,
        IdGenerator idGen,
        IOptions<HoldOptions> holdOptions,
        IOptions<DatabaseOptions> dbOptions,
        ClassQueryCoalescer coalescer,
        AppMetrics? metrics = null,
        ClassWriteGate? writeGate = null)
    {
        _store = store;
        _idGen = idGen;
        _holdOptions = holdOptions;
        _dbOptions = dbOptions;
        _coalescer = coalescer;
        _metrics = metrics;
        _writeGate = writeGate ?? new ClassWriteGate(holdOptions);
    }

    private int HoldDurationSeconds => _holdOptions.Value.HoldDurationSeconds;
    private int LockTimeoutSeconds => _dbOptions.Value.LockTimeoutSeconds;

    public async Task<CreateHoldResponse> CreateHoldAsync(string classId, string studentId, CancellationToken ct = default)
    {
        // Per-class virtual waiting room: bounds concurrent DB lock waiters per class to protect connection pool
        using var gateLease = await _writeGate.EnterAsync(classId, TimeSpan.FromSeconds(LockTimeoutSeconds), ct);

        await using var session = await _store.OpenSessionAsync(classId, ct);

        // 1. Fast path: unlocked pre-check to reject full or non-existent classes without lock contention
        var fastCheck = await session.CheckCapacityAsync(ct);
        if (!fastCheck.Found)
            throw AppException.NotFound($"Class '{classId}' not found.");
        if (fastCheck.ActiveCount >= fastCheck.TotalSeats)
        {
            _metrics?.HoldsRejected.Add(1, new KeyValuePair<string, object?>("reason", "SEATS_FULL"));
            throw AppException.NoSeatsAvailable();
        }

        var holdId = _idGen.NewHoldId();
        var duration = HoldDurationSeconds;

        try
        {
            await using var tx = await session.BeginLockedAsync(LockTimeoutSeconds, ct);
            try
            {
                // 2. Lock class row
                var totalSeats = await tx.LockClassAsync(classId, ct);
                if (totalSeats is null)
                    throw AppException.NotFound($"Class '{classId}' not found.");

                // 3. Sync database clock
                var now = await tx.GetDbClockAsync(ct);

                // 4. Expire stale holds for this class inside lock
                await tx.ExpireStaleHoldsAsync(classId, now, ct);

                // 5. Invariant: student can only have 1 active hold per class
                var hasActive = await tx.HasActiveHoldForStudentAsync(classId, studentId, ct);
                if (hasActive)
                {
                    _metrics?.HoldsRejected.Add(1, new KeyValuePair<string, object?>("reason", "ACTIVE_HOLD_EXISTS"));
                    throw AppException.AlreadyApplied();
                }

                // 6. Invariant: held + confirmed <= totalSeats
                var activeCount = await tx.CountActiveSeatsAsync(classId, ct);
                if (activeCount >= totalSeats.Value)
                {
                    _metrics?.HoldsRejected.Add(1, new KeyValuePair<string, object?>("reason", "SEATS_FULL"));
                    throw AppException.NoSeatsAvailable();
                }

                // 7. Insert new hold
                var expiresAt = now.AddSeconds(duration);
                await tx.InsertHoldAsync(holdId, classId, studentId, expiresAt, now, ct);

                // 8. Commit with CancellationToken.None to preserve durability against client aborts
                await tx.CommitAsync(CancellationToken.None);
                _coalescer.OnWriteOccurred(classId);
                _metrics?.HoldsCreated.Add(1);

                return new CreateHoldResponse(
                    holdId,
                    classId,
                    studentId,
                    HoldStatus.Held,
                    DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc),
                    DateTime.SpecifyKind(now, DateTimeKind.Utc));
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        catch (Exception ex) when (PostgresErrorClassifier.IsBusy(ex, ct))
        {
            throw AppException.TemporarilyBusy();
        }
    }

    public async Task<HoldResponse> GetHoldAsync(string holdId, CancellationToken ct = default)
    {
        var record = await _store.GetHoldByIdAsync(holdId, ct);
        if (record is null)
            throw AppException.NotFound($"Hold '{holdId}' not found.");

        var derivedStatus = HoldStateMachine.DeriveStatus(record.Status, record.ExpiresAt, record.ServerTime);
        return new HoldResponse(record.Id, record.ClassId, derivedStatus, record.ExpiresAt, record.ServerTime);
    }

    public async Task<ConfirmHoldResponse> ConfirmHoldAsync(string holdId, string studentId, CancellationToken ct = default)
    {
        await using var session = await _store.OpenSessionAsync(ct: ct);

        var hold = await session.GetHoldForOwnershipCheckAsync(holdId, ct);
        if (hold is null)
            throw AppException.NotFound($"Hold '{holdId}' not found.");

        if (hold.StudentId != studentId)
            throw AppException.NotYourHold();

        try
        {
            await using var tx = await session.BeginLockedAsync(LockTimeoutSeconds, ct);
            try
            {
                await tx.LockClassAsync(hold.ClassId, ct);

                var now = await tx.GetDbClockAsync(ct);
                var lockState = await tx.LockHoldRowAsync(holdId, ct);

                if (!HoldStateMachine.ValidateConfirmation(lockState.Status, lockState.ExpiresAt, now, out var error))
                {
                    if (error is not null)
                        throw error;
                }

                if (lockState.Status == HoldStatus.Confirmed)
                {
                    await tx.CommitAsync(CancellationToken.None);
                    _metrics?.ConfirmsTotal.Add(1, new KeyValuePair<string, object?>("result", "ALREADY_CONFIRMED"));
                    return new ConfirmHoldResponse(holdId, HoldStatus.Confirmed);
                }

                var rows = await tx.ConfirmHoldAsync(holdId, now, ct);
                if (rows == 0)
                {
                    var recheck = await tx.LockHoldRowAsync(holdId, ct);
                    if (recheck.Status == HoldStatus.Confirmed)
                    {
                        await tx.CommitAsync(CancellationToken.None);
                        return new ConfirmHoldResponse(holdId, HoldStatus.Confirmed);
                    }
                    throw AppException.HoldExpired();
                }

                await tx.CommitAsync(CancellationToken.None);
                _coalescer.OnWriteOccurred(hold.ClassId);
                _metrics?.ConfirmsTotal.Add(1, new KeyValuePair<string, object?>("result", "CONFIRMED"));
                if (hold.CreatedAt != default)
                {
                    _metrics?.HoldDurationSeconds.Record((now - hold.CreatedAt).TotalSeconds);
                }

                return new ConfirmHoldResponse(holdId, HoldStatus.Confirmed);
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        catch (Exception ex) when (PostgresErrorClassifier.IsBusy(ex, ct))
        {
            throw AppException.TemporarilyBusy();
        }
    }

    public async Task CancelHoldAsync(string holdId, string studentId, CancellationToken ct = default)
    {
        await using var session = await _store.OpenSessionAsync(ct: ct);

        var hold = await session.GetHoldForOwnershipCheckAsync(holdId, ct);
        if (hold is null)
            throw AppException.NotFound($"Hold '{holdId}' not found.");

        if (hold.StudentId != studentId)
            throw AppException.NotYourHold();

        if (!HoldStateMachine.ValidateCancellation(hold.Status, out var isAlreadyDone, out var err))
        {
            if (err is not null)
                throw err;
        }
        if (isAlreadyDone)
            return;

        try
        {
            await using var tx = await session.BeginLockedAsync(LockTimeoutSeconds, ct);
            try
            {
                await tx.LockClassAsync(hold.ClassId, ct);

                var now = await tx.GetDbClockAsync(ct);
                var lockState = await tx.LockHoldRowAsync(holdId, ct);

                if (!HoldStateMachine.ValidateCancellation(lockState.Status, out var doneInLock, out var lockErr))
                {
                    if (lockErr is not null)
                        throw lockErr;
                }
                if (doneInLock)
                {
                    await tx.CommitAsync(CancellationToken.None);
                    return;
                }

                await tx.CancelHoldAsync(holdId, now, ct);
                await tx.CommitAsync(CancellationToken.None);
                _coalescer.OnWriteOccurred(hold.ClassId);
                _metrics?.CancelsTotal.Add(1);
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        catch (Exception ex) when (PostgresErrorClassifier.IsBusy(ex, ct))
        {
            throw AppException.TemporarilyBusy();
        }
    }

    public async Task<List<MyHoldResponse>> GetMyHoldsAsync(string studentId, CancellationToken ct = default)
    {
        return await _store.GetHoldsForStudentAsync(studentId, ct);
    }
}
