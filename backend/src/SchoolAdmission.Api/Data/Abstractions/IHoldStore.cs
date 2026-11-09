using SchoolAdmission.Api.Contracts;

namespace SchoolAdmission.Api.Data.Abstractions;

public record FastPathSeatCheckResult(bool Found, int TotalSeats, int ActiveCount);

public record HoldRecord(string Id, string ClassId, string Status, DateTime ExpiresAt, DateTime ServerTime);

public record HoldOwnershipRecord(string ClassId, string Status, string StudentId, DateTime ExpiresAt, DateTime CreatedAt = default);

public record HoldLockState(string Status, DateTime ExpiresAt);

public interface IHoldSession : IAsyncDisposable
{
    Task<FastPathSeatCheckResult> CheckCapacityAsync(CancellationToken ct = default);
    Task<IHoldTransaction> BeginLockedAsync(int lockTimeoutSeconds, CancellationToken ct = default);
}

public interface IHoldTransaction : IAsyncDisposable
{
    Task SetLockTimeoutAsync(int timeoutSeconds, CancellationToken ct = default);
    Task<int?> LockClassAsync(string classId, CancellationToken ct = default);
    Task<DateTime> GetDbClockAsync(CancellationToken ct = default);
    Task<int> ExpireStaleHoldsAsync(string classId, DateTime now, CancellationToken ct = default);
    Task<bool> HasActiveHoldForStudentAsync(string classId, string studentId, CancellationToken ct = default);
    Task<int> CountActiveSeatsAsync(string classId, CancellationToken ct = default);
    Task InsertHoldAsync(string id, string classId, string studentId, DateTime expiresAt, DateTime now, CancellationToken ct = default);
    Task<HoldLockState> LockHoldRowAsync(string holdId, CancellationToken ct = default);
    Task ConfirmHoldAsync(string holdId, DateTime now, CancellationToken ct = default);
    Task CancelHoldAsync(string holdId, DateTime now, CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

public interface IHoldStore
{
    Task<IHoldSession> OpenSessionAsync(string classId, CancellationToken ct = default);
    Task<FastPathSeatCheckResult> CheckFastPathSeatsAsync(string classId, CancellationToken ct = default);
    Task<HoldRecord?> GetHoldByIdAsync(string holdId, CancellationToken ct = default);
    Task<HoldOwnershipRecord?> GetHoldForOwnershipCheckAsync(string holdId, CancellationToken ct = default);
    Task<List<MyHoldResponse>> GetHoldsForStudentAsync(string studentId, CancellationToken ct = default);
    Task<IHoldTransaction> BeginHoldTransactionAsync(int lockTimeoutSeconds, CancellationToken ct = default);
}
