using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Data.Abstractions;
using SchoolAdmission.Api.Domain;

namespace SchoolAdmission.Tests;

public class TestDatabaseFixture : IAsyncLifetime
{
    public string DatabaseName { get; } = $"school_admission_test_{Guid.NewGuid():N}";
    private string AdminConnectionString { get; set; } = "";
    public string ConnectionString { get; private set; } = "";
    public bool PostgresAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        AdminConnectionString = Environment.GetEnvironmentVariable("TEST_ADMIN_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

        if (!await TryCreateDatabaseAsync())
        {
            await TryStartDockerPostgresAsync();
            await TryCreateDatabaseAsync();
        }

        if (!PostgresAvailable)
        {
            // Fallback connection string key so in-memory stores isolate per fixture instance
            ConnectionString = $"Host=localhost;Port=5432;Database={DatabaseName};Username=postgres;Password=postgres;InMemoryFallback=true";
        }
    }

    private async Task<bool> TryCreateDatabaseAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(AdminConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\"", conn);
            await cmd.ExecuteNonQueryAsync();

            var cb = new NpgsqlConnectionStringBuilder(AdminConnectionString)
            {
                Database = DatabaseName
            };
            ConnectionString = cb.ConnectionString;
            PostgresAvailable = true;
            return true;
        }
        catch
        {
            PostgresAvailable = false;
            return false;
        }
    }

    private static async Task TryStartDockerPostgresAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("docker", "compose up -d db")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
            }

            // Wait briefly for Postgres readiness
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    await using var conn = new NpgsqlConnection("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres;Timeout=2");
                    await conn.OpenAsync();
                    return;
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
        }
        catch
        {
            // Docker CLI not available or daemon stopped; fallback store will handle API tests
        }
    }

    public async Task DisposeAsync()
    {
        InMemoryTestStoreRegistry.Remove(ConnectionString);
        if (!PostgresAvailable) return;

        try
        {
            await using var conn = new NpgsqlConnection(AdminConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($@"
                SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{DatabaseName}';
                DROP DATABASE IF EXISTS ""{DatabaseName}"";", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

public class SchoolAdmissionFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly int _holdDurationSeconds;
    private readonly bool _rateLimitEnabled;
    private readonly int _rateLimitPerStudent;
    private readonly string? _adminApiKey;
    private readonly string? _studentAuthSecret;
    private readonly bool _useInMemoryFallback;

    public SchoolAdmissionFactory(
        string connectionString,
        int holdDurationSeconds = 600,
        bool rateLimitEnabled = false,
        int rateLimitPerStudent = 10,
        string? adminApiKey = null,
        string? studentAuthSecret = null)
    {
        _useInMemoryFallback = string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("InMemoryFallback=true");
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? "Host=localhost;Port=5432;Database=inmemory_default;Username=postgres;Password=postgres;InMemoryFallback=true"
            : connectionString.Replace(";InMemoryFallback=true", "");
        _holdDurationSeconds = holdDurationSeconds;
        _rateLimitEnabled = rateLimitEnabled;
        _rateLimitPerStudent = rateLimitPerStudent;
        _adminApiKey = adminApiKey;
        _studentAuthSecret = studentAuthSecret;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            var inMem = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["HOLD_DURATION_SECONDS"] = _holdDurationSeconds.ToString(),
                ["CORS_ALLOWED_ORIGINS"] = "http://localhost:3000",
                ["RATE_LIMIT_ENABLED"] = _rateLimitEnabled.ToString(),
                ["RATE_LIMIT_HOLDS_PER_MINUTE_PER_STUDENT"] = _rateLimitPerStudent.ToString()
            };
            if (_useInMemoryFallback)
            {
                inMem["SKIP_DB_MIGRATION"] = "true";
                inMem["SWEEPER_ENABLED"] = "false";
            }
            if (!string.IsNullOrEmpty(_adminApiKey))
            {
                inMem["ADMIN_API_KEY"] = _adminApiKey;
            }
            if (!string.IsNullOrEmpty(_studentAuthSecret))
            {
                inMem["STUDENT_AUTH_SECRET"] = _studentAuthSecret;
            }
            config.AddInMemoryCollection(inMem);
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(_connectionString));

            if (_useInMemoryFallback)
            {
                var state = InMemoryTestStoreRegistry.GetOrCreate(_connectionString);
                services.AddScoped<IClassStore>(_ => new InMemoryTestClassStore(state));
                services.AddScoped<IHoldStore>(_ => new InMemoryTestHoldStore(state));
            }
        });
    }
}

internal static class InMemoryTestStoreRegistry
{
    private static readonly ConcurrentDictionary<string, InMemoryDatabaseState> Stores = new();

    public static InMemoryDatabaseState GetOrCreate(string key) =>
        Stores.GetOrAdd(key, _ => new InMemoryDatabaseState());

    public static void Remove(string key)
    {
        if (!string.IsNullOrEmpty(key))
            Stores.TryRemove(key.Replace(";InMemoryFallback=true", ""), out _);
    }
}

internal sealed class InMemoryDatabaseState
{
    public readonly SemaphoreSlim GlobalLock = new(1, 1);
    public readonly List<Class> Classes = new();
    public readonly List<Hold> Holds = new();
}

internal sealed class InMemoryTestClassStore : IClassStore
{
    private readonly InMemoryDatabaseState _state;

    public InMemoryTestClassStore(InMemoryDatabaseState state) => _state = state;

    public async Task<ClassResponse> CreateClassAsync(string id, string name, int totalSeats, DateTime createdAt, CancellationToken ct = default)
    {
        await _state.GlobalLock.WaitAsync(ct);
        try
        {
            _state.Classes.Add(new Class { Id = id, Name = name, TotalSeats = totalSeats, CreatedAt = createdAt });
            return new ClassResponse(id, name, totalSeats);
        }
        finally
        {
            _state.GlobalLock.Release();
        }
    }

    public async Task<List<ClassDetailResponse>> GetAllClassesAsync(CancellationToken ct = default)
    {
        await _state.GlobalLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            return _state.Classes
                .OrderBy(c => c.CreatedAt)
                .Select(c =>
                {
                    var held = _state.Holds.Count(h => h.ClassId == c.Id && h.Status == HoldStatus.Held && h.ExpiresAt > now);
                    var confirmed = _state.Holds.Count(h => h.ClassId == c.Id && h.Status == HoldStatus.Confirmed);
                    return new ClassDetailResponse(c.Id, c.Name, c.TotalSeats, held, confirmed, c.TotalSeats - held - confirmed);
                })
                .ToList();
        }
        finally
        {
            _state.GlobalLock.Release();
        }
    }

    public async Task<ClassDetailResponse?> GetClassByIdAsync(string classId, CancellationToken ct = default)
    {
        await _state.GlobalLock.WaitAsync(ct);
        try
        {
            var cls = _state.Classes.FirstOrDefault(c => c.Id == classId);
            if (cls is null) return null;
            var now = DateTime.UtcNow;
            var held = _state.Holds.Count(h => h.ClassId == cls.Id && h.Status == HoldStatus.Held && h.ExpiresAt > now);
            var confirmed = _state.Holds.Count(h => h.ClassId == cls.Id && h.Status == HoldStatus.Confirmed);
            return new ClassDetailResponse(cls.Id, cls.Name, cls.TotalSeats, held, confirmed, cls.TotalSeats - held - confirmed);
        }
        finally
        {
            _state.GlobalLock.Release();
        }
    }
}

internal sealed class InMemoryTestHoldStore : IHoldStore
{
    private readonly InMemoryDatabaseState _state;

    public InMemoryTestHoldStore(InMemoryDatabaseState state) => _state = state;

    public Task<IHoldSession> OpenSessionAsync(string? classId = null, CancellationToken ct = default) =>
        Task.FromResult<IHoldSession>(new InMemoryTestHoldSession(_state, classId ?? string.Empty));

    public async Task<FastPathSeatCheckResult> CheckFastPathSeatsAsync(string classId, CancellationToken ct = default)
    {
        await _state.GlobalLock.WaitAsync(ct);
        try
        {
            var cls = _state.Classes.FirstOrDefault(c => c.Id == classId);
            if (cls is null) return new FastPathSeatCheckResult(false, 0, 0);
            var now = DateTime.UtcNow;
            var active = _state.Holds.Count(h => h.ClassId == classId && (h.Status == HoldStatus.Confirmed || (h.Status == HoldStatus.Held && h.ExpiresAt > now)));
            return new FastPathSeatCheckResult(true, cls.TotalSeats, active);
        }
        finally
        {
            _state.GlobalLock.Release();
        }
    }

    public async Task<HoldRecord?> GetHoldByIdAsync(string holdId, CancellationToken ct = default)
    {
        await _state.GlobalLock.WaitAsync(ct);
        try
        {
            var h = _state.Holds.FirstOrDefault(x => x.Id == holdId);
            if (h is null) return null;
            return new HoldRecord(h.Id, h.ClassId, h.Status, h.ExpiresAt, DateTime.UtcNow);
        }
        finally
        {
            _state.GlobalLock.Release();
        }
    }

    public async Task<HoldOwnershipRecord?> GetHoldForOwnershipCheckAsync(string holdId, CancellationToken ct = default)
    {
        await _state.GlobalLock.WaitAsync(ct);
        try
        {
            var h = _state.Holds.FirstOrDefault(x => x.Id == holdId);
            if (h is null) return null;
            return new HoldOwnershipRecord(h.ClassId, h.Status, h.StudentId, h.ExpiresAt, h.CreatedAt);
        }
        finally
        {
            _state.GlobalLock.Release();
        }
    }

    public async Task<List<MyHoldResponse>> GetHoldsForStudentAsync(string studentId, CancellationToken ct = default)
    {
        await _state.GlobalLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            return _state.Holds
                .Where(h => h.StudentId == studentId)
                .OrderByDescending(h => h.CreatedAt)
                .Select(h =>
                {
                    var clsName = _state.Classes.FirstOrDefault(c => c.Id == h.ClassId)?.Name ?? h.ClassId;
                    var derived = HoldStateMachine.DeriveStatus(h.Status, h.ExpiresAt, now);
                    return new MyHoldResponse(h.Id, h.ClassId, clsName, derived, h.ExpiresAt);
                })
                .ToList();
        }
        finally
        {
            _state.GlobalLock.Release();
        }
    }

    public async Task<IHoldTransaction> BeginHoldTransactionAsync(int lockTimeoutSeconds, CancellationToken ct = default)
    {
        await _state.GlobalLock.WaitAsync(TimeSpan.FromSeconds(lockTimeoutSeconds), ct);
        return new InMemoryTestHoldTransaction(_state);
    }

    private sealed class InMemoryTestHoldSession : IHoldSession
    {
        private readonly InMemoryDatabaseState _state;
        private readonly string _classId;
        private readonly InMemoryTestHoldStore _store;

        public InMemoryTestHoldSession(InMemoryDatabaseState state, string classId)
        {
            _state = state;
            _classId = classId;
            _store = new InMemoryTestHoldStore(state);
        }

        public Task<FastPathSeatCheckResult> CheckCapacityAsync(CancellationToken ct = default) =>
            _store.CheckFastPathSeatsAsync(_classId, ct);

        public Task<HoldOwnershipRecord?> GetHoldForOwnershipCheckAsync(string holdId, CancellationToken ct = default) =>
            _store.GetHoldForOwnershipCheckAsync(holdId, ct);

        public Task<IHoldTransaction> BeginLockedAsync(int lockTimeoutSeconds, CancellationToken ct = default) =>
            _store.BeginHoldTransactionAsync(lockTimeoutSeconds, ct);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InMemoryTestHoldTransaction : IHoldTransaction
    {
        private readonly InMemoryDatabaseState _state;
        private bool _released;

        public InMemoryTestHoldTransaction(InMemoryDatabaseState state) => _state = state;

        public Task SetLockTimeoutAsync(int timeoutSeconds, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int?> LockClassAsync(string classId, CancellationToken ct = default) =>
            Task.FromResult(_state.Classes.FirstOrDefault(c => c.Id == classId)?.TotalSeats);

        public Task<DateTime> GetDbClockAsync(CancellationToken ct = default) => Task.FromResult(DateTime.UtcNow);

        public Task<int> ExpireStaleHoldsAsync(string classId, DateTime now, CancellationToken ct = default)
        {
            int count = 0;
            foreach (var h in _state.Holds.Where(h => h.ClassId == classId && h.Status == HoldStatus.Held && h.ExpiresAt <= now))
            {
                h.Status = HoldStatus.Expired;
                count++;
            }
            return Task.FromResult(count);
        }

        public Task<bool> HasActiveHoldForStudentAsync(string classId, string studentId, CancellationToken ct = default) =>
            Task.FromResult(_state.Holds.Any(h => h.ClassId == classId && h.StudentId == studentId && (h.Status == HoldStatus.Held || h.Status == HoldStatus.Confirmed)));

        public Task<int> CountActiveSeatsAsync(string classId, CancellationToken ct = default) =>
            Task.FromResult(_state.Holds.Count(h => h.ClassId == classId && (h.Status == HoldStatus.Held || h.Status == HoldStatus.Confirmed)));

        public Task InsertHoldAsync(string id, string classId, string studentId, DateTime expiresAt, DateTime now, CancellationToken ct = default)
        {
            _state.Holds.Add(new Hold
            {
                Id = id,
                ClassId = classId,
                StudentId = studentId,
                Status = HoldStatus.Held,
                ExpiresAt = expiresAt,
                CreatedAt = now
            });
            return Task.CompletedTask;
        }

        public Task<HoldLockState> LockHoldRowAsync(string holdId, CancellationToken ct = default)
        {
            var h = _state.Holds.First(x => x.Id == holdId);
            return Task.FromResult(new HoldLockState(h.Status, h.ExpiresAt));
        }

        public Task<int> ConfirmHoldAsync(string holdId, DateTime now, CancellationToken ct = default)
        {
            var h = _state.Holds.FirstOrDefault(x => x.Id == holdId && x.Status == HoldStatus.Held && x.ExpiresAt > now);
            if (h is null) return Task.FromResult(0);
            h.Status = HoldStatus.Confirmed;
            h.ConfirmedAt = now;
            return Task.FromResult(1);
        }

        public Task CancelHoldAsync(string holdId, DateTime now, CancellationToken ct = default)
        {
            var h = _state.Holds.FirstOrDefault(x => x.Id == holdId && x.Status == HoldStatus.Held);
            if (h is not null)
            {
                h.Status = HoldStatus.Cancelled;
                h.CancelledAt = now;
            }
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct = default)
        {
            Release();
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct = default)
        {
            Release();
            return Task.CompletedTask;
        }

        private void Release()
        {
            if (!_released)
            {
                _released = true;
                _state.GlobalLock.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            Release();
            return ValueTask.CompletedTask;
        }
    }
}
