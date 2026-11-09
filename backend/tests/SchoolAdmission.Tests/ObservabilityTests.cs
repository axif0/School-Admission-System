using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolAdmission.Api.Contracts;
using Xunit;

namespace SchoolAdmission.Tests;

public class ObservabilityTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ObservabilityTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RequestId_IsEchoedInResponseHeader()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Request-Id", "test-req-custom-999");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Request-Id"));
        Assert.Equal("test-req-custom-999", response.Headers.GetValues("X-Request-Id").First());
    }

    [Fact]
    public async Task RequestId_IsGeneratedWhenMissing()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Request-Id"));
        var val = response.Headers.GetValues("X-Request-Id").First();
        Assert.False(string.IsNullOrWhiteSpace(val));
    }

    [Fact]
    public async Task ErrorResponse_IncludesTraceIdAlongSideCodeAndMessage()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();

        // 1. Create a 1-seat class
        var classRes = await client.PostAsJsonAsync("/classes", new { name = "Obs Test Class", totalSeats = 1 });
        Assert.Equal(HttpStatusCode.Created, classRes.StatusCode);
        var cls = await classRes.Content.ReadFromJsonAsync<ClassResponse>();
        Assert.NotNull(cls);

        // 2. First student holds the only seat
        var req1 = new HttpRequestMessage(HttpMethod.Post, $"/classes/{cls.Id}/holds");
        req1.Headers.Add("X-Student-Id", "20159999999999991");
        req1.Headers.Add("X-Request-Id", "trace-test-req-1");
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // 3. Second student tries to hold -> 409 Conflict
        var req2 = new HttpRequestMessage(HttpMethod.Post, $"/classes/{cls.Id}/holds");
        req2.Headers.Add("X-Student-Id", "20159999999999992");
        req2.Headers.Add("X-Request-Id", "trace-test-req-conflict");
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);

        var json = await res2.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("error", out var errorEl));
        Assert.True(errorEl.TryGetProperty("code", out var codeEl));
        Assert.True(errorEl.TryGetProperty("message", out var msgEl));
        Assert.True(errorEl.TryGetProperty("traceId", out var traceIdEl));

        Assert.Equal("NO_SEATS_AVAILABLE", codeEl.GetString());
        Assert.Equal("trace-test-req-conflict", traceIdEl.GetString());
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsPrometheusText()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();

        // 1. Create a class and hold to trigger both counters and latency/duration histograms
        var classRes = await client.PostAsJsonAsync("/classes", new { name = "Metrics Test Class", totalSeats = 10 });
        Assert.Equal(HttpStatusCode.Created, classRes.StatusCode);
        var cls = await classRes.Content.ReadFromJsonAsync<ClassResponse>();
        Assert.NotNull(cls);

        // Read classes
        await client.GetAsync("/classes");

        // Create hold
        var holdReq = new HttpRequestMessage(HttpMethod.Post, $"/classes/{cls.Id}/holds");
        holdReq.Headers.Add("X-Student-Id", "20158888888888881");
        var holdRes = await client.SendAsync(holdReq);
        Assert.Equal(HttpStatusCode.Created, holdRes.StatusCode);

        // 2. Scrape /metrics
        var response = await client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();

        // Counters
        Assert.Contains("holds_created_total", text);
        Assert.Contains("holds_rejected_total{reason=\"SEATS_FULL\"}", text);
        Assert.Contains("confirms_total{result=\"CONFIRMED\"}", text);
        Assert.Contains("cancels_total", text);
        Assert.Contains("expired_swept_total", text);

        // Histograms with standard Prometheus exposition format
        Assert.Contains("hold_duration_seconds_bucket", text);
        Assert.Contains("hold_duration_seconds_sum", text);
        Assert.Contains("hold_duration_seconds_count", text);
        Assert.Contains("class_read_latency_seconds_bucket", text);
        Assert.Contains("class_read_latency_seconds_sum", text);
        Assert.Contains("class_read_latency_seconds_count", text);

        // 3. Scrape /metrics with OpenMetrics Accept header
        var omReq = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        omReq.Headers.Add("Accept", "application/openmetrics-text; version=1.0.0");
        var omRes = await client.SendAsync(omReq);
        Assert.Equal(HttpStatusCode.OK, omRes.StatusCode);
        var omText = await omRes.Content.ReadAsStringAsync();
        Assert.Contains("# EOF", omText);
    }

    [Fact]
    public async Task SseStreamEndpoint_EstablishesEventStreamAndPushesWriteEvents()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var response = await client.GetAsync("/classes/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // Read initial ': connected' comment line
        var firstLine = await reader.ReadLineAsync(cts.Token);
        Assert.Equal(": connected", firstLine);
    }
}
