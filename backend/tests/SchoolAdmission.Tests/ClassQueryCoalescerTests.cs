using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Services;
using Xunit;

namespace SchoolAdmission.Tests;

public class ClassQueryCoalescerTests
{
    [Fact]
    public async Task Coalescer_SharesInFlightQueries_BetweenConcurrentCallers()
    {
        var coalescer = new ClassQueryCoalescer();
        int queryExecutionCount = 0;

        var tcs = new TaskCompletionSource();

        async Task<List<ClassDetailResponse>> SlowQuery()
        {
            Interlocked.Increment(ref queryExecutionCount);
            await tcs.Task; // Hold execution in-flight
            return new List<ClassDetailResponse>();
        }

        // Fire 5 concurrent requests while the first is in-flight
        var task1 = coalescer.ExecuteCoalescedAsync(SlowQuery);
        var task2 = coalescer.ExecuteCoalescedAsync(SlowQuery);
        var task3 = coalescer.ExecuteCoalescedAsync(SlowQuery);
        var task4 = coalescer.ExecuteCoalescedAsync(SlowQuery);
        var task5 = coalescer.ExecuteCoalescedAsync(SlowQuery);

        // Allow query to complete
        tcs.SetResult();

        await Task.WhenAll(task1, task2, task3, task4, task5);

        // All 5 callers shared the EXACT SAME single database query execution!
        Assert.Equal(1, queryExecutionCount);
    }

    [Fact]
    public async Task Coalescer_BypassesStaleFlight_WhenWriteOccursToPreserveReadYourWrites()
    {
        var coalescer = new ClassQueryCoalescer();
        int queryExecutionCount = 0;

        var tcs = new TaskCompletionSource();

        async Task<List<ClassDetailResponse>> SlowQuery()
        {
            Interlocked.Increment(ref queryExecutionCount);
            await tcs.Task;
            return new List<ClassDetailResponse>();
        }

        // 1. Caller A starts a query
        var callerA = coalescer.ExecuteCoalescedAsync(SlowQuery);

        // 2. A write occurs while Caller A's query is in-flight!
        coalescer.OnWriteOccurred();

        // 3. Caller B arrives AFTER the write
        // Caller B must NOT join Caller A's old flight because that would yield stale data.
        var callerB = coalescer.ExecuteCoalescedAsync(() =>
        {
            Interlocked.Increment(ref queryExecutionCount);
            return Task.FromResult(new List<ClassDetailResponse>());
        });

        tcs.SetResult();
        await Task.WhenAll(callerA, callerB);

        // Caller B triggered a fresh execution!
        Assert.Equal(2, queryExecutionCount);
    }
}
