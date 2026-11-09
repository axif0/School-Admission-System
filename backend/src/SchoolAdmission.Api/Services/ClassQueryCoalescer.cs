using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using SchoolAdmission.Api.Contracts;

namespace SchoolAdmission.Api.Services;

/// <summary>
/// Coalesces concurrent in-flight GET /classes and GET /classes/{classId} database queries
/// so that hundreds of simultaneous requests share a single database round-trip.
/// Preserves strict Read-Your-Writes semantics via monotonic global and per-class write versions.
/// Also broadcasts real-time Server-Sent Events (SSE) notifications on state mutations.
/// </summary>
public class ClassQueryCoalescer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private long _globalWriteVersion = 0;
    private readonly object _globalLock = new();
    private Task<(IReadOnlyList<ClassDetailResponse> List, byte[] JsonBytes)>? _currentGlobalFlight;
    private long _currentGlobalFlightVersion;

    private sealed class ClassFlightEntry
    {
        public readonly object Lock = new();
        public long WriteVersion;
        public Task<ClassDetailResponse?>? CurrentFlight;
        public long CurrentFlightVersion;
    }

    private const int MaxTrackedClassFlights = 4096;
    private readonly ConcurrentDictionary<string, ClassFlightEntry> _classFlights = new();
    private readonly ConcurrentDictionary<Guid, Channel<string>> _sseSubscribers = new();

    public void OnWriteOccurred(string? classId = null)
    {
        Interlocked.Increment(ref _globalWriteVersion);

        if (!string.IsNullOrEmpty(classId))
        {
            var entry = GetOrCreateClassEntry(classId);
            Interlocked.Increment(ref entry.WriteVersion);
        }
        else
        {
            foreach (var kvp in _classFlights)
            {
                Interlocked.Increment(ref kvp.Value.WriteVersion);
            }
        }

        BroadcastUpdate(classId ?? "all");
    }

    private ClassFlightEntry GetOrCreateClassEntry(string classId)
    {
        if (_classFlights.TryGetValue(classId, out var existing))
            return existing;

        if (_classFlights.Count >= MaxTrackedClassFlights)
        {
            foreach (var kvp in _classFlights)
            {
                if (kvp.Value.CurrentFlight == null || kvp.Value.CurrentFlight.IsCompleted)
                {
                    _classFlights.TryRemove(kvp.Key, out _);
                }
            }
        }

        return _classFlights.GetOrAdd(classId, _ => new ClassFlightEntry());
    }

    public (Guid SubscriptionId, ChannelReader<string> Reader) SubscribeUpdates()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _sseSubscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void UnsubscribeUpdates(Guid subscriptionId)
    {
        if (_sseSubscribers.TryRemove(subscriptionId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    private void BroadcastUpdate(string target)
    {
        foreach (var kvp in _sseSubscribers)
        {
            kvp.Value.Writer.TryWrite(target);
        }
    }

    public async Task<(IReadOnlyList<ClassDetailResponse> List, byte[] JsonBytes)> ExecuteCoalescedWithBytesAsync(
        Func<CancellationToken, Task<List<ClassDetailResponse>>> queryFactory,
        CancellationToken callerCt = default)
    {
        Task<(IReadOnlyList<ClassDetailResponse> List, byte[] JsonBytes)> flight;

        lock (_globalLock)
        {
            var currentVersion = Interlocked.Read(ref _globalWriteVersion);

            if (_currentGlobalFlight != null && !_currentGlobalFlight.IsCompleted && _currentGlobalFlightVersion >= currentVersion)
            {
                flight = _currentGlobalFlight;
            }
            else
            {
                _currentGlobalFlightVersion = currentVersion;
                flight = QueryAndResetGlobalAsync(() => queryFactory(CancellationToken.None));
                _currentGlobalFlight = flight;
            }
        }

        if (callerCt.CanBeCanceled)
        {
            return await flight.WaitAsync(callerCt);
        }

        return await flight;
    }

    public async Task<IReadOnlyList<ClassDetailResponse>> ExecuteCoalescedAsync(
        Func<CancellationToken, Task<List<ClassDetailResponse>>> queryFactory,
        CancellationToken callerCt = default)
    {
        var (list, _) = await ExecuteCoalescedWithBytesAsync(queryFactory, callerCt);
        return list;
    }

    public Task<IReadOnlyList<ClassDetailResponse>> ExecuteCoalescedAsync(
        Func<Task<List<ClassDetailResponse>>> queryFactory,
        CancellationToken callerCt = default)
    {
        return ExecuteCoalescedAsync(_ => queryFactory(), callerCt);
    }

    public async Task<ClassDetailResponse?> ExecuteCoalescedByIdAsync(
        string classId,
        Func<CancellationToken, Task<ClassDetailResponse?>> queryFactory,
        CancellationToken callerCt = default)
    {
        var entry = GetOrCreateClassEntry(classId);
        Task<ClassDetailResponse?> flight;

        lock (entry.Lock)
        {
            var currentVersion = Interlocked.Read(ref entry.WriteVersion);

            if (entry.CurrentFlight != null && !entry.CurrentFlight.IsCompleted && entry.CurrentFlightVersion >= currentVersion)
            {
                flight = entry.CurrentFlight;
            }
            else
            {
                entry.CurrentFlightVersion = currentVersion;
                flight = QueryAndResetClassAsync(entry, () => queryFactory(CancellationToken.None));
                entry.CurrentFlight = flight;
            }
        }

        if (callerCt.CanBeCanceled)
        {
            return await flight.WaitAsync(callerCt);
        }

        return await flight;
    }

    private async Task<(IReadOnlyList<ClassDetailResponse> List, byte[] JsonBytes)> QueryAndResetGlobalAsync(
        Func<Task<List<ClassDetailResponse>>> queryFactory)
    {
        try
        {
            var result = await queryFactory();
            var readOnly = result.AsReadOnly();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result, JsonOpts);
            return (readOnly, bytes);
        }
        finally
        {
            lock (_globalLock)
            {
                if (_currentGlobalFlight?.IsCompleted == true)
                {
                    _currentGlobalFlight = null;
                }
            }
        }
    }

    private static async Task<ClassDetailResponse?> QueryAndResetClassAsync(
        ClassFlightEntry entry,
        Func<Task<ClassDetailResponse?>> queryFactory)
    {
        try
        {
            return await queryFactory();
        }
        finally
        {
            lock (entry.Lock)
            {
                if (entry.CurrentFlight?.IsCompleted == true)
                {
                    entry.CurrentFlight = null;
                }
            }
        }
    }
}
