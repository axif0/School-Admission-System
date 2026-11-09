using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Middleware;

namespace SchoolAdmission.Api.Services;

/// <summary>
/// Application-tier per-class virtual waiting room / concurrency gate.
/// Prevents a single-class flash stampede from exhausting the shared PostgreSQL connection pool
/// by metering concurrent write transactions per classId in an async FIFO queue before DB connection checkout.
/// Uses a fixed striped array of semaphores to guarantee strictly O(1) bounded memory regardless of group count.
/// </summary>
public class ClassWriteGate
{
    private const int StripeCount = 1024;
    private readonly SemaphoreSlim[] _stripes;
    private readonly int _maxConcurrencyPerClass;

    public ClassWriteGate(IOptions<HoldOptions>? options = null)
        : this(options?.Value.MaxConcurrentWritesPerClass ?? 32)
    {
    }

    public ClassWriteGate(int maxConcurrencyPerClass)
    {
        _maxConcurrencyPerClass = Math.Max(1, maxConcurrencyPerClass);
        _stripes = new SemaphoreSlim[StripeCount];
        for (int i = 0; i < StripeCount; i++)
        {
            _stripes[i] = new SemaphoreSlim(_maxConcurrencyPerClass, _maxConcurrencyPerClass);
        }
    }

    public async ValueTask<Lease> EnterAsync(string classId, TimeSpan timeout, CancellationToken ct = default)
    {
        var index = (uint)StringComparer.Ordinal.GetHashCode(classId ?? string.Empty) % StripeCount;
        var gate = _stripes[index];
        var acquired = await gate.WaitAsync(timeout, ct);
        if (!acquired)
        {
            throw AppException.TemporarilyBusy();
        }
        return new Lease(gate);
    }

    public readonly struct Lease : IDisposable
    {
        private readonly SemaphoreSlim? _gate;

        public Lease(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            _gate?.Release();
        }
    }
}
