using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolAdmission.Api.Contracts;

namespace SchoolAdmission.Tests;

public class ValidationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ValidationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task MissingOrBlankStudentId_Returns400InvalidStudentId()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        // 1. Missing header
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/classes/c_unknown/holds");
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.BadRequest, res1.StatusCode);
        var err1 = await res1.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts);
        Assert.Equal("INVALID_STUDENT_ID", err1!.Error.Code);

        // 2. Whitespace only header
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/classes/c_unknown/holds");
        req2.Headers.Add("X-Student-Id", "   ");
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
        var err2 = await res2.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts);
        Assert.Equal("INVALID_STUDENT_ID", err2!.Error.Code);
    }

    [Fact]
    public async Task InvalidClassBody_Returns400ValidationError()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        // Total seats <= 0
        var res1 = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Bad Class", 0));
        Assert.Equal(HttpStatusCode.BadRequest, res1.StatusCode);
        var err1 = await res1.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts);
        Assert.Equal("VALIDATION_ERROR", err1!.Error.Code);

        // Blank name
        var res2 = await client.PostAsJsonAsync("/classes", new CreateClassRequest("   ", 50));
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
        var err2 = await res2.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts);
        Assert.Equal("VALIDATION_ERROR", err2!.Error.Code);
    }

    [Fact]
    public async Task NonExistentClassOrHold_Returns404NotFound()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        var res1 = await client.GetAsync("/classes/c_nonexistent123");
        Assert.Equal(HttpStatusCode.NotFound, res1.StatusCode);
        var err1 = await res1.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts);
        Assert.Equal("NOT_FOUND", err1!.Error.Code);

        var res2 = await client.GetAsync("/holds/h_nonexistent123");
        Assert.Equal(HttpStatusCode.NotFound, res2.StatusCode);
        var err2 = await res2.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts);
        Assert.Equal("NOT_FOUND", err2!.Error.Code);
    }

    [Fact]
    public async Task PostHoldsAndConfirm_AcceptsEmptyBody()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        var createRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Empty Body Test", 5));
        var cls = await createRes.Content.ReadFromJsonAsync<ClassResponse>(JsonOpts);

        // POST /classes/{id}/holds with null/empty content and no Content-Type
        var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{cls!.Id}/holds")
        {
            Content = null
        };
        req.Headers.Add("X-Student-Id", "20152691234567890");

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task ForeignKey_OrphanHoldInsert_FailsWith23503()
    {
        if (!_fixture.PostgresAvailable) return;

        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();
        await client.GetAsync("/health/ready"); // ensures migrations run and DB is seeded

        // Direct SQL insert with nonexistent class ID
        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(@"
            INSERT INTO holds (id, class_id, student_id, status, expires_at, created_at)
            VALUES ('h_orphan_test', 'c_non_existent_class', '20150000000000099', 'HELD', now() + interval '10 min', now())", conn);

        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("23503", ex.SqlState);
    }
}
