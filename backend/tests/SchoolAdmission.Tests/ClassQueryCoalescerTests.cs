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

    [Fact]
    public async Task Coalescer_PerClassInvalidation_WriteToClassA_DoesNotInvalidateInFlightQueryForClassB()
    {
        var coalescer = new ClassQueryCoalescer();
        int classAExecutions = 0;
        int classBExecutions = 0;

        var tcsA = new TaskCompletionSource();
        var tcsB = new TaskCompletionSource();

        // Start in-flight queries for both Class A and Class B
        var flightA1 = coalescer.ExecuteCoalescedByIdAsync("c_A", async _ =>
        {
            Interlocked.Increment(ref classAExecutions);
            await tcsA.Task;
            return new ClassDetailResponse("c_A", "Class A", 10, 0, 0, 10);
        });

        var flightB1 = coalescer.ExecuteCoalescedByIdAsync("c_B", async _ =>
        {
            Interlocked.Increment(ref classBExecutions);
            await tcsB.Task;
            return new ClassDetailResponse("c_B", "Class B", 10, 0, 0, 10);
        });

        // A write occurs ONLY on Class A!
        coalescer.OnWriteOccurred("c_A");

        // Second caller for Class B must still join flightB1 (unaffected by Class A write)
        var flightB2 = coalescer.ExecuteCoalescedByIdAsync("c_B", _ =>
        {
            Interlocked.Increment(ref classBExecutions);
            return Task.FromResult<ClassDetailResponse?>(new ClassDetailResponse("c_B", "Class B Fresh", 10, 0, 0, 10));
        });

        // Second caller for Class A must start a fresh flight (since Class A was mutated)
        var flightA2 = coalescer.ExecuteCoalescedByIdAsync("c_A", _ =>
        {
            Interlocked.Increment(ref classAExecutions);
            return Task.FromResult<ClassDetailResponse?>(new ClassDetailResponse("c_A", "Class A Updated", 10, 1, 0, 9));
        });

        tcsA.SetResult();
        tcsB.SetResult();

        await Task.WhenAll(flightA1, flightA2, flightB1, flightB2);

        Assert.Equal(2, classAExecutions); // Invalidated and re-queried
        Assert.Equal(1, classBExecutions); // Coalesced! Not invalidated by Class A write
    }

    [Fact]
    public async Task Coalescer_SseSubscription_BroadcastsClassIdOnWrite()
    {
        var coalescer = new ClassQueryCoalescer();
        var (subId, reader) = coalescer.SubscribeUpdates();
        try
        {
            coalescer.OnWriteOccurred("c_target_99");

            var msg = await reader.ReadAsync();
            Assert.Equal("c_target_99", msg);
        }
        finally
        {
            coalescer.UnsubscribeUpdates(subId);
        }
    }

    [Fact]
    public async Task Coalescer_ManyDistinctClassIds_EvictsCompletedEntriesToBoundMemory()
    {
        var coalescer = new ClassQueryCoalescer();

        for (int i = 0; i < 6000; i++)
        {
            var id = $"c_group_{i}";
            var res = await coalescer.ExecuteCoalescedByIdAsync(id, _ =>
                Task.FromResult<ClassDetailResponse?>(new ClassDetailResponse(id, $"Group {i}", 10, 0, 0, 10)));
            Assert.NotNull(res);
        }

        // Subsequent queries still function accurately after eviction sweeps
        var final = await coalescer.ExecuteCoalescedByIdAsync("c_group_5999", _ =>
            Task.FromResult<ClassDetailResponse?>(new ClassDetailResponse("c_group_5999", "Group 5999", 10, 1, 0, 9)));
        Assert.Equal(9, final!.AvailableSeats);
    }
}
