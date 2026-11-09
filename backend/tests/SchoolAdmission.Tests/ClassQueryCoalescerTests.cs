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

    [Fact]
    public async Task Coalescer_CallerCancellationIsolation_CallerACancels_CallerBReceivesData_QueryExecutesOnce()
    {
        var coalescer = new ClassQueryCoalescer();
        int queryExecutionCount = 0;
        var tcs = new TaskCompletionSource();

        async Task<List<ClassDetailResponse>> SlowQuery(CancellationToken _)
        {
            Interlocked.Increment(ref queryExecutionCount);
            await tcs.Task;
            return new List<ClassDetailResponse>
            {
                new("c_1", "Test Class 1", 10, 0, 0, 10)
            };
        }

        using var ctsA = new CancellationTokenSource();

        // Caller A starts query with its own token
        var taskA = coalescer.ExecuteCoalescedAsync(SlowQuery, ctsA.Token);

        // Caller B joins the flight with default token
        var taskB = coalescer.ExecuteCoalescedAsync(SlowQuery, CancellationToken.None);

        // Caller A aborts connection
        ctsA.Cancel();

        // Assert task A throws OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => taskA);

        // Underlying database query completes
        tcs.SetResult();

        // Caller B still succeeds and gets data
        var resultB = await taskB;
        Assert.Single(resultB);
        Assert.Equal("Test Class 1", resultB[0].Name);

        // Query was executed only once!
        Assert.Equal(1, queryExecutionCount);
    }

    [Fact]
    public async Task Coalescer_ReturnedListIsImmutable_MutationsThrowNotSupportedException()
    {
        var coalescer = new ClassQueryCoalescer();
        var result = await coalescer.ExecuteCoalescedAsync(() => Task.FromResult(new List<ClassDetailResponse>
        {
            new("c_1", "Test Class 1", 10, 0, 0, 10)
        }));

        Assert.IsAssignableFrom<IReadOnlyList<ClassDetailResponse>>(result);

        // Attempting to cast to IList to mutate elements must throw NotSupportedException
        if (result is IList<ClassDetailResponse> mutableList)
        {
            Assert.Throws<NotSupportedException>(() =>
                mutableList.Add(new ClassDetailResponse("c_2", "Malicious Injected Class", 10, 0, 0, 10)));
            Assert.Throws<NotSupportedException>(() =>
                mutableList[0] = new ClassDetailResponse("c_1", "Overwritten Class", 10, 0, 0, 10));
        }
    }
}
