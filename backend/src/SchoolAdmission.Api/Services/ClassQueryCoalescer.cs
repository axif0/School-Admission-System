using SchoolAdmission.Api.Contracts;

namespace SchoolAdmission.Api.Services;

/// <summary>
/// Coalesces concurrent in-flight GET /classes database queries so that hundreds
/// of simultaneous polling requests share a single database round-trip.
/// Preserves strict Read-Your-Writes semantics via a monotonically increasing write version.
/// Enforces immutability of returned data and isolates caller cancellations.
/// </summary>
public class ClassQueryCoalescer
{
    private long _writeVersion = 0;
    private readonly object _lock = new();
    private Task<IReadOnlyList<ClassDetailResponse>>? _currentFlight;
    private long _currentFlightVersion;

    public void OnWriteOccurred()
    {
        Interlocked.Increment(ref _writeVersion);
    }

    public async Task<IReadOnlyList<ClassDetailResponse>> ExecuteCoalescedAsync(
        Func<CancellationToken, Task<List<ClassDetailResponse>>> queryFactory,
        CancellationToken callerCt = default)
    {
        Task<IReadOnlyList<ClassDetailResponse>> flight;

        lock (_lock)
        {
            var currentVersion = Interlocked.Read(ref _writeVersion);

            // If a flight is active AND started at or after the latest write, join it
            if (_currentFlight != null && !_currentFlight.IsCompleted && _currentFlightVersion >= currentVersion)
            {
                flight = _currentFlight;
            }
            else
            {
                // Start a new flight with the current write version.
                // The underlying query executes with CancellationToken.None to prevent one caller's abort
                // from failing concurrent callers.
                _currentFlightVersion = currentVersion;
                flight = QueryAndResetAsync(() => queryFactory(CancellationToken.None));
                _currentFlight = flight;
            }
        }

        // Caller awaits the flight isolated by their own caller token
        if (callerCt.CanBeCanceled)
        {
            return await flight.WaitAsync(callerCt);
        }

        return await flight;
    }

    public Task<IReadOnlyList<ClassDetailResponse>> ExecuteCoalescedAsync(
        Func<Task<List<ClassDetailResponse>>> queryFactory,
        CancellationToken callerCt = default)
    {
        return ExecuteCoalescedAsync(_ => queryFactory(), callerCt);
    }

    private async Task<IReadOnlyList<ClassDetailResponse>> QueryAndResetAsync(
        Func<Task<List<ClassDetailResponse>>> queryFactory)
    {
        try
        {
            var result = await queryFactory();
            // Return immutable read-only view
            return result.AsReadOnly();
        }
        finally
        {
            lock (_lock)
            {
                // Reset completed or faulted flight so subsequent calls start fresh
                if (_currentFlight?.IsCompleted == true)
                {
                    _currentFlight = null;
                }
            }
        }
    }
}
