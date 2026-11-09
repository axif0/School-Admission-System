using Microsoft.Extensions.Options;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data.Abstractions;
using SchoolAdmission.Api.Domain;
using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Api.Services;
using Xunit;

namespace SchoolAdmission.Tests;

public class HoldServiceUnitTests
{
    private class FakeHoldTransaction : IHoldTransaction
    {
        public int? TotalSeatsToReturn { get; set; } = 10;
        public int ActiveSeatsCount { get; set; } = 0;
        public bool HasActiveHold { get; set; } = false;
        public DateTime Clock { get; set; } = DateTime.UtcNow;
        public HoldLockState HoldLockStateToReturn { get; set; } = new(HoldStatus.Held, DateTime.UtcNow.AddMinutes(10));
        public bool Committed { get; private set; }
        public bool Rollbacked { get; private set; }
        public bool Inserted { get; private set; }
        public bool Confirmed { get; private set; }
        public bool Cancelled { get; private set; }

        public Task SetLockTimeoutAsync(int timeoutSeconds, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int?> LockClassAsync(string classId, CancellationToken ct = default) => Task.FromResult(TotalSeatsToReturn);
        public Task<DateTime> GetDbClockAsync(CancellationToken ct = default) => Task.FromResult(Clock);
        public Task<int> ExpireStaleHoldsAsync(string classId, DateTime now, CancellationToken ct = default) => Task.FromResult(0);
        public Task<bool> HasActiveHoldForStudentAsync(string classId, string studentId, CancellationToken ct = default) => Task.FromResult(HasActiveHold);
        public Task<int> CountActiveSeatsAsync(string classId, CancellationToken ct = default) => Task.FromResult(ActiveSeatsCount);
        public Task InsertHoldAsync(string id, string classId, string studentId, DateTime expiresAt, DateTime now, CancellationToken ct = default)
        {
            Inserted = true;
            return Task.CompletedTask;
        }
        public Task<HoldLockState> LockHoldRowAsync(string holdId, CancellationToken ct = default) => Task.FromResult(HoldLockStateToReturn);
        public Task ConfirmHoldAsync(string holdId, DateTime now, CancellationToken ct = default)
        {
            Confirmed = true;
            return Task.CompletedTask;
        }
        public Task CancelHoldAsync(string holdId, DateTime now, CancellationToken ct = default)
        {
            Cancelled = true;
            return Task.CompletedTask;
        }
        public Task CommitAsync(CancellationToken ct = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }
        public Task RollbackAsync(CancellationToken ct = default)
        {
            Rollbacked = true;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private class FakeHoldSession : IHoldSession
    {
        private readonly FakeHoldStore _store;

        public FakeHoldSession(FakeHoldStore store)
        {
            _store = store;
        }

        public Task<FastPathSeatCheckResult> CheckCapacityAsync(CancellationToken ct = default) =>
            Task.FromResult(_store.FastPathResult);

        public Task<IHoldTransaction> BeginLockedAsync(int lockTimeoutSeconds, CancellationToken ct = default)
        {
            _store.LockedBegunCount++;
            return Task.FromResult<IHoldTransaction>(_store.Tx);
        }

        public ValueTask DisposeAsync()
        {
            _store.SessionsDisposedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private class FakeHoldStore : IHoldStore
    {
        public FakeHoldTransaction Tx { get; set; } = new();
        public FastPathSeatCheckResult FastPathResult { get; set; } = new(true, 10, 0);
        public HoldOwnershipRecord? OwnershipRecord { get; set; }
        public int SessionsOpenedCount { get; set; }
        public int SessionsDisposedCount { get; set; }
        public int LockedBegunCount { get; set; }

        public Task<IHoldSession> OpenSessionAsync(string classId, CancellationToken ct = default)
        {
            SessionsOpenedCount++;
            return Task.FromResult<IHoldSession>(new FakeHoldSession(this));
        }

        public Task<FastPathSeatCheckResult> CheckFastPathSeatsAsync(string classId, CancellationToken ct = default) =>
            Task.FromResult(FastPathResult);

        public Task<HoldRecord?> GetHoldByIdAsync(string holdId, CancellationToken ct = default) =>
            Task.FromResult<HoldRecord?>(null);

        public Task<HoldOwnershipRecord?> GetHoldForOwnershipCheckAsync(string holdId, CancellationToken ct = default) =>
            Task.FromResult(OwnershipRecord);

        public Task<List<MyHoldResponse>> GetHoldsForStudentAsync(string studentId, CancellationToken ct = default) =>
            Task.FromResult(new List<MyHoldResponse>());

        public Task<IHoldTransaction> BeginHoldTransactionAsync(int lockTimeoutSeconds, CancellationToken ct = default) =>
            Task.FromResult<IHoldTransaction>(Tx);
    }

    private readonly FakeHoldStore _fakeStore = new();
    private readonly IdGenerator _idGen = new();
    private readonly IOptions<HoldOptions> _holdOptions = Options.Create(new HoldOptions());
    private readonly IOptions<DatabaseOptions> _dbOptions = Options.Create(new DatabaseOptions());
    private readonly ClassQueryCoalescer _coalescer = new();

    private HoldService CreateSvc() => new(_fakeStore, _idGen, _holdOptions, _dbOptions, _coalescer);

    [Fact]
    public async Task CreateHold_FullClass_ThrowsNoSeatsAvailable()
    {
        // Active seats equals total seats
        _fakeStore.Tx.TotalSeatsToReturn = 10;
        _fakeStore.Tx.ActiveSeatsCount = 10;

        var svc = CreateSvc();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            svc.CreateHoldAsync("c_1", "20150000000000001"));

        Assert.Equal("NO_SEATS_AVAILABLE", ex.Code);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task CreateHold_DuplicateStudent_ThrowsAlreadyApplied()
    {
        _fakeStore.Tx.TotalSeatsToReturn = 10;
        _fakeStore.Tx.ActiveSeatsCount = 2;
        _fakeStore.Tx.HasActiveHold = true; // Duplicate

        var svc = CreateSvc();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            svc.CreateHoldAsync("c_1", "20150000000000001"));

        Assert.Equal("ALREADY_APPLIED", ex.Code);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task ConfirmHold_NotYourHold_ThrowsNotYourHold()
    {
        _fakeStore.OwnershipRecord = new HoldOwnershipRecord("c_1", HoldStatus.Held, "20151111111111111", DateTime.UtcNow.AddMinutes(5));

        var svc = CreateSvc();

        // Caller student ID is different from owner
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            svc.ConfirmHoldAsync("h_1", "20159999999999999"));

        Assert.Equal("NOT_YOUR_HOLD", ex.Code);
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task ConfirmHold_ExpiredHold_ThrowsHoldExpired()
    {
        var past = DateTime.UtcNow.AddSeconds(-10);
        _fakeStore.OwnershipRecord = new HoldOwnershipRecord("c_1", HoldStatus.Held, "20151111111111111", past);
        _fakeStore.Tx.HoldLockStateToReturn = new HoldLockState(HoldStatus.Held, past);
        _fakeStore.Tx.Clock = DateTime.UtcNow;

        var svc = CreateSvc();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            svc.ConfirmHoldAsync("h_1", "20151111111111111"));

        Assert.Equal("HOLD_EXPIRED", ex.Code);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task ConfirmHold_AlreadyConfirmed_ReturnsIdempotentSuccess()
    {
        _fakeStore.OwnershipRecord = new HoldOwnershipRecord("c_1", HoldStatus.Confirmed, "20151111111111111", DateTime.UtcNow.AddMinutes(5));
        _fakeStore.Tx.HoldLockStateToReturn = new HoldLockState(HoldStatus.Confirmed, DateTime.UtcNow.AddMinutes(5));

        var svc = CreateSvc();

        var res = await svc.ConfirmHoldAsync("h_1", "20151111111111111");

        Assert.Equal("h_1", res.HoldId);
        Assert.Equal(HoldStatus.Confirmed, res.Status);
        Assert.True(_fakeStore.Tx.Committed);
    }

    [Fact]
    public async Task CreateHold_SingleConnectionPerRequest_OnSuccessAndFastPathReject()
    {
        // 1. Success path: exactly 1 session opened and disposed
        _fakeStore.FastPathResult = new FastPathSeatCheckResult(true, 10, 0);
        _fakeStore.Tx.TotalSeatsToReturn = 10;
        _fakeStore.Tx.ActiveSeatsCount = 0;
        _fakeStore.Tx.HasActiveHold = false;

        var svc = CreateSvc();
        var res = await svc.CreateHoldAsync("c_1", "20150000000000001");
        Assert.NotNull(res);
        Assert.Equal(1, _fakeStore.SessionsOpenedCount);
        Assert.Equal(1, _fakeStore.LockedBegunCount);
        Assert.Equal(1, _fakeStore.SessionsDisposedCount);

        // 2. Fast-path rejection path: exactly 1 session opened and disposed, 0 locked transactions begun
        _fakeStore.FastPathResult = new FastPathSeatCheckResult(true, 10, 10); // definitely full
        await Assert.ThrowsAsync<AppException>(() => svc.CreateHoldAsync("c_1", "20150000000000002"));
        Assert.Equal(2, _fakeStore.SessionsOpenedCount); // +1
        Assert.Equal(1, _fakeStore.LockedBegunCount); // still 1 (no transaction begun)
        Assert.Equal(2, _fakeStore.SessionsDisposedCount); // +1 (disposed immediately)
    }
}
