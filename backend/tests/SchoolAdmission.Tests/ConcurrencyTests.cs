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
        if (!_fixture.PostgresAvailable) return;

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
        if (!_fixture.PostgresAvailable) return;

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
}
