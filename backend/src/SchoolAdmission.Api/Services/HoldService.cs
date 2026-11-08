using Microsoft.Extensions.Options;
using Npgsql;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data.Abstractions;
using SchoolAdmission.Api.Domain;
using SchoolAdmission.Api.Middleware;

namespace SchoolAdmission.Api.Services;

public class HoldService : IHoldService
{
    private readonly IHoldStore _store;
    private readonly IdGenerator _idGen;
    private readonly IOptions<HoldOptions> _holdOptions;
    private readonly IOptions<DatabaseOptions> _dbOptions;
    private readonly ClassQueryCoalescer _coalescer;

    public HoldService(
        IHoldStore store,
        IdGenerator idGen,
        IOptions<HoldOptions> holdOptions,
        IOptions<DatabaseOptions> dbOptions,
        ClassQueryCoalescer coalescer)
    {
        _store = store;
        _idGen = idGen;
        _holdOptions = holdOptions;
        _dbOptions = dbOptions;
        _coalescer = coalescer;
    }

    private int HoldDurationSeconds => _holdOptions.Value.HoldDurationSeconds;
    private int LockTimeoutSeconds => _dbOptions.Value.LockTimeoutSeconds;

    public async Task<CreateHoldResponse> CreateHoldAsync(string classId, string studentId, CancellationToken ct = default)
    {
        // 1. Fast path: unlocked pre-check to reject full classes without lock contention
        var fastCheck = await _store.CheckFastPathSeatsAsync(classId, ct);
        if (fastCheck.Found && fastCheck.ActiveCount >= fastCheck.TotalSeats)
            throw AppException.NoSeatsAvailable();

        var holdId = _idGen.NewHoldId();
        var duration = HoldDurationSeconds;

        try
        {
            await using var tx = await _store.BeginHoldTransactionAsync(LockTimeoutSeconds, ct);
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
                    throw AppException.AlreadyApplied();

                // 6. Invariant: held + confirmed <= totalSeats
                var activeCount = await tx.CountActiveSeatsAsync(classId, ct);
                if (activeCount >= totalSeats.Value)
                    throw AppException.NoSeatsAvailable();

                // 7. Insert new hold
                var expiresAt = now.AddSeconds(duration);
                await tx.InsertHoldAsync(holdId, classId, studentId, expiresAt, now, ct);

                // 8. Commit with CancellationToken.None to preserve durability against client aborts
                await tx.CommitAsync(CancellationToken.None);
                _coalescer.OnWriteOccurred();

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
        catch (NpgsqlException ex) when (ex.Message.Contains("lock timeout") || ex.Message.Contains("canceling statement"))
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
        // 1. Ownership & existence pre-check
        var hold = await _store.GetHoldForOwnershipCheckAsync(holdId, ct);
        if (hold is null)
            throw AppException.NotFound($"Hold '{holdId}' not found.");

        if (hold.StudentId != studentId)
            throw AppException.NotYourHold();

        try
        {
            await using var tx = await _store.BeginHoldTransactionAsync(LockTimeoutSeconds, ct);
            try
            {
                // Lock class row to synchronize seat transitions
                await tx.LockClassAsync(hold.ClassId, ct);

                var now = await tx.GetDbClockAsync(ct);
                var lockState = await tx.LockHoldRowAsync(holdId, ct);

                // Domain State Machine validation
                if (!HoldStateMachine.ValidateConfirmation(lockState.Status, lockState.ExpiresAt, now, out var error))
                {
                    if (error is not null)
                        throw error;
                }

                // If already confirmed, idempotent return
                if (lockState.Status == HoldStatus.Confirmed)
                {
                    await tx.CommitAsync(CancellationToken.None);
                    return new ConfirmHoldResponse(holdId, HoldStatus.Confirmed);
                }

                await tx.ConfirmHoldAsync(holdId, now, ct);
                await tx.CommitAsync(CancellationToken.None);
                _coalescer.OnWriteOccurred();

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
    }

    public async Task CancelHoldAsync(string holdId, string studentId, CancellationToken ct = default)
    {
        // 1. Ownership & existence pre-check
        var hold = await _store.GetHoldForOwnershipCheckAsync(holdId, ct);
        if (hold is null)
            throw AppException.NotFound($"Hold '{holdId}' not found.");

        if (hold.StudentId != studentId)
            throw AppException.NotYourHold();

        // Fast-path state check
        if (!HoldStateMachine.ValidateCancellation(hold.Status, out var isAlreadyDone, out var err))
        {
            if (err is not null)
                throw err;
        }
        if (isAlreadyDone)
            return;

        try
        {
            await using var tx = await _store.BeginHoldTransactionAsync(LockTimeoutSeconds, ct);
            try
            {
                // Lock class row
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
                _coalescer.OnWriteOccurred();
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
    }

    public async Task<List<MyHoldResponse>> GetMyHoldsAsync(string studentId, CancellationToken ct = default)
    {
        return await _store.GetHoldsForStudentAsync(studentId, ct);
    }
}
