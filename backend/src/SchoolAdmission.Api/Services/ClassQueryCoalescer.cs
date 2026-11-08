using SchoolAdmission.Api.Contracts;

namespace SchoolAdmission.Api.Services;

/// <summary>
/// Coalesces concurrent in-flight GET /classes database queries so that hundreds
/// of simultaneous polling requests share a single database round-trip.
/// Preserves strict Read-Your-Writes semantics via a monotonically increasing write version.
/// </summary>
public class ClassQueryCoalescer
{
    private long _writeVersion = 0;
    private readonly object _lock = new();
    private Task<List<ClassDetailResponse>>? _currentFlight;
    private long _currentFlightVersion;

    public void OnWriteOccurred()
    {
        Interlocked.Increment(ref _writeVersion);
    }

    public Task<List<ClassDetailResponse>> ExecuteCoalescedAsync(
        Func<Task<List<ClassDetailResponse>>> queryFactory)
    {
        lock (_lock)
        {
            var currentVersion = Interlocked.Read(ref _writeVersion);

            // If a flight is active AND started at or after the latest write, join it
            if (_currentFlight != null && !_currentFlight.IsCompleted && _currentFlightVersion >= currentVersion)
            {
                return _currentFlight;
            }

            // Start a new flight with the current write version
            _currentFlightVersion = currentVersion;
            var flight = QueryAndResetAsync(queryFactory);
            _currentFlight = flight;
            return flight;
        }
    }

    private async Task<List<ClassDetailResponse>> QueryAndResetAsync(
        Func<Task<List<ClassDetailResponse>>> queryFactory)
    {
        try
        {
            return await queryFactory();
        }
        finally
        {
            lock (_lock)
            {
                if (_currentFlight?.IsCompleted == true)
                {
                    _currentFlight = null;
                }
            }
        }
    }
}
