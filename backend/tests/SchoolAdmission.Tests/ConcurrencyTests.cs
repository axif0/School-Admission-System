using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolAdmission.Api.Contracts;

namespace SchoolAdmission.Tests;

public class ConcurrencyTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ConcurrencyTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task NoOverselling_UnderHeavyParallelLoad_NeverExceedsTotalSeats()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        // 1. Create a class with 10 seats
        var createClassRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Concurrency Class", 10));
        Assert.Equal(HttpStatusCode.Created, createClassRes.StatusCode);
        var created = await createClassRes.Content.ReadFromJsonAsync<ClassResponse>(JsonOpts);
        Assert.NotNull(created);
        var classId = created.Id;

        // 2. Fire 150 parallel hold requests with distinct student IDs
        const int totalRequests = 150;
        var tasks = Enumerable.Range(1, totalRequests).Select(async i =>
        {
            var studentId = $"2026{i:D13}"; // 17-digit format
            var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            request.Headers.Add("X-Student-Id", studentId);
            return await client.SendAsync(request);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        // 3. Exactly 10 must succeed (201 Created), remaining must be 409 Conflict
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(10, successCount);
        Assert.Equal(totalRequests - 10, conflictCount);

        // 4. Verify class seat counts
        var classDetailRes = await client.GetAsync($"/classes/{classId}");
        Assert.Equal(HttpStatusCode.OK, classDetailRes.StatusCode);
        var detail = await classDetailRes.Content.ReadFromJsonAsync<ClassDetailResponse>(JsonOpts);
        Assert.NotNull(detail);

        Assert.Equal(10, detail.TotalSeats);
        Assert.Equal(10, detail.HeldSeats);
        Assert.Equal(0, detail.ConfirmedSeats);
        Assert.Equal(0, detail.AvailableSeats);
    }

    [Fact]
    public async Task SameStudent_ParallelRequests_OnlyOneSucceeds()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        // Create class with 50 seats
        var createClassRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Single Student Parallel", 50));
        var created = await createClassRes.Content.ReadFromJsonAsync<ClassResponse>(JsonOpts);
        var classId = created!.Id;

        const string studentId = "20152691234567890";
        const int requests = 20;

        var tasks = Enumerable.Range(1, requests).Select(async _ =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            req.Headers.Add("X-Student-Id", studentId);
            return await client.SendAsync(req);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, successCount);
        Assert.Equal(requests - 1, conflictCount);

        // Verify conflict code is ALREADY_APPLIED
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            var err = await conflict.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts);
            Assert.NotNull(err);
            Assert.Equal("ALREADY_APPLIED", err.Error.Code);
        }
    }

    [Fact]
    public async Task MixedOps_HoldConfirmCancel_ConcurrentlyNeverExceedsSeats()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        // 10-seat class
        var createRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Mixed Ops Class", 10));
        var created = await createRes.Content.ReadFromJsonAsync<ClassResponse>(JsonOpts);
        var classId = created!.Id;

        const int studentCount = 50;
        var tasks = Enumerable.Range(1, studentCount).Select(async i =>
        {
            var studentId = $"20261111{i:D9}";
            // 1. Try to hold
            var holdReq = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            holdReq.Headers.Add("X-Student-Id", studentId);
            var holdRes = await client.SendAsync(holdReq);

            if (holdRes.StatusCode == HttpStatusCode.Created)
            {
                var holdBody = await holdRes.Content.ReadFromJsonAsync<CreateHoldResponse>(JsonOpts);
                var holdId = holdBody!.HoldId;

                // Alternate between confirm and cancel
                if (i % 2 == 0)
                {
                    var confReq = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
                    confReq.Headers.Add("X-Student-Id", studentId);
                    await client.SendAsync(confReq);
                }
                else
                {
                    var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdId}");
                    delReq.Headers.Add("X-Student-Id", studentId);
                    await client.SendAsync(delReq);
                }
            }
        });

        await Task.WhenAll(tasks);

        var detailRes = await client.GetAsync($"/classes/{classId}");
        var detail = await detailRes.Content.ReadFromJsonAsync<ClassDetailResponse>(JsonOpts);
        Assert.NotNull(detail);

        Assert.Equal(10, detail.TotalSeats);
        Assert.True(detail.HeldSeats + detail.ConfirmedSeats <= 10, "Held + confirmed exceeded total seats!");
        Assert.True(detail.AvailableSeats >= 0);
        Assert.Equal(10 - (detail.HeldSeats + detail.ConfirmedSeats), detail.AvailableSeats);
    }

    [Fact]
    public async Task TwoSeparateBackendInstances_AgainstSameDb_ZeroOverbooking()
    {
        // Simulate Section 1.6 Bonus: two separate backend process instances hitting the same database
        using var factory1 = new SchoolAdmissionFactory(_fixture.ConnectionString);
        using var factory2 = new SchoolAdmissionFactory(_fixture.ConnectionString);

        var client1 = factory1.CreateClient();
        var client2 = factory2.CreateClient();

        // 1. Create a 5-seat class on Instance 1
        var createRes = await client1.PostAsJsonAsync("/classes", new CreateClassRequest("Multi-Host Bonus Class", 5));
        var created = await createRes.Content.ReadFromJsonAsync<ClassResponse>(JsonOpts);
        var classId = created!.Id;

        // 2. Fire 30 requests to Instance 1 and 30 requests to Instance 2 concurrently
        var t1 = Enumerable.Range(1, 30).Select(async i =>
        {
            var studentId = $"20262222{i:D9}";
            var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            req.Headers.Add("X-Student-Id", studentId);
            return await client1.SendAsync(req);
        });

        var t2 = Enumerable.Range(31, 30).Select(async i =>
        {
            var studentId = $"20263333{i:D9}";
            var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            req.Headers.Add("X-Student-Id", studentId);
            return await client2.SendAsync(req);
        });

        var allResponses = await Task.WhenAll(t1.Concat(t2));

        var totalCreated = allResponses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var totalConflict = allResponses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(5, totalCreated);
        Assert.Equal(55, totalConflict);

        // Verify state through Instance 2
        var checkRes = await client2.GetAsync($"/classes/{classId}");
        var detail = await checkRes.Content.ReadFromJsonAsync<ClassDetailResponse>(JsonOpts);
        Assert.NotNull(detail);
        Assert.Equal(5, detail.HeldSeats);
        Assert.Equal(0, detail.AvailableSeats);
    }

    [Fact]
    public async Task SimultaneousStartup_CompetingOnMigration_AdvisoryLockEnsuresSingleRun()
    {
        // Launch two migrations simultaneously against the same DB
        using var f1 = new SchoolAdmissionFactory(_fixture.ConnectionString);
        using var f2 = new SchoolAdmissionFactory(_fixture.ConnectionString);

        var t1 = Task.Run(async () =>
        {
            using var c = f1.CreateClient();
            return await c.GetAsync("/health/ready");
        });

        var t2 = Task.Run(async () =>
        {
            using var c = f2.CreateClient();
            return await c.GetAsync("/health/ready");
        });

        var results = await Task.WhenAll(t1, t2);
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
