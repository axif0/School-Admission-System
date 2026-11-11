using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolAdmission.Api.Contracts;
using Xunit;

namespace SchoolAdmission.Tests;

public class RateLimiterTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RateLimiterTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RateLimiter_WhenEnabled_RejectsExceedingRequestsWith429AndRetryAfter()
    {
        // Factory with Rate Limit ENABLED: limit of 2 holds per minute for a student
        using var factory = new SchoolAdmissionFactory(
            _fixture.ConnectionString,
            holdDurationSeconds: 600,
            rateLimitEnabled: true,
            rateLimitPerStudent: 2);

        var client = factory.CreateClient();

        // Create a test class with 50 seats
        var classRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("RateLimit Test", 50));
        Assert.Equal(HttpStatusCode.Created, classRes.StatusCode);
        var cls = await classRes.Content.ReadFromJsonAsync<ClassResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(cls);

        var studentId = "20159999999999991";
        client.DefaultRequestHeaders.Add("X-Student-Id", studentId);

        // 1st request: OK (201)
        var req1 = await client.PostAsync($"/classes/{cls.Id}/holds", null);
        Assert.Equal(HttpStatusCode.Created, req1.StatusCode);

        // Cancel hold so student can apply again
        var hold1 = await req1.Content.ReadFromJsonAsync<CreateHoldResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        await client.DeleteAsync($"/holds/{hold1!.HoldId}");

        // 2nd request: OK (201)
        var req2 = await client.PostAsync($"/classes/{cls.Id}/holds", null);
        Assert.Equal(HttpStatusCode.Created, req2.StatusCode);

        // 3rd request (exceeds permit limit of 2): 429 Too Many Requests!
        var req3 = await client.PostAsync($"/classes/{cls.Id}/holds", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, req3.StatusCode);
        Assert.True(req3.Headers.Contains("Retry-After"));
        Assert.Equal("60", req3.Headers.GetValues("Retry-After").First());

        var body = await req3.Content.ReadAsStringAsync();
        Assert.Contains("TOO_MANY_REQUESTS", body);
    }

    [Fact]
    public async Task RateLimiter_WhenDisabled_AllowsExceedingRequestsWithout429()
    {
        // Factory with Rate Limit DISABLED (default)
        using var factory = new SchoolAdmissionFactory(
            _fixture.ConnectionString,
            holdDurationSeconds: 600,
            rateLimitEnabled: false);

        var client = factory.CreateClient();

        var classRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("NoRateLimit Test", 50));
        Assert.Equal(HttpStatusCode.Created, classRes.StatusCode);
        var cls = await classRes.Content.ReadFromJsonAsync<ClassResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(cls);

        // Send multiple requests from distinct students
        for (int i = 1; i <= 5; i++)
        {
            var studentId = $"2015000000000000{i}";
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{cls.Id}/holds");
            req.Headers.Add("X-Student-Id", studentId);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }
    }
}
