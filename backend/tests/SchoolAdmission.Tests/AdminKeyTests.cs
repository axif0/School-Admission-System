using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolAdmission.Api.Contracts;
using Xunit;

namespace SchoolAdmission.Tests;

public class AdminKeyTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AdminKeyTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WhenAdminKeyUnset_PostClassesSucceedsWithoutHeader()
    {
        if (!_fixture.PostgresAvailable) return;

        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString, adminApiKey: null);
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Unauthenticated Class", 30));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task WhenAdminKeyConfigured_EnforcesAuthentication()
    {
        if (!_fixture.PostgresAvailable) return;

        const string configuredSecret = "my-super-secret-admin-key-2026";
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString, adminApiKey: configuredSecret);
        var client = factory.CreateClient();

        // 1. Missing header -> 401 ADMIN_KEY_REQUIRED
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/classes")
        {
            Content = JsonContent.Create(new CreateClassRequest("Secured Class 1", 30))
        };
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Unauthorized, res1.StatusCode);
        var err1 = await res1.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts);
        Assert.NotNull(err1);
        Assert.Equal("ADMIN_KEY_REQUIRED", err1.Error.Code);

        // 2. Wrong header -> 403 ADMIN_KEY_INVALID
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/classes")
        {
            Content = JsonContent.Create(new CreateClassRequest("Secured Class 2", 30))
        };
        req2.Headers.Add("X-Admin-Key", "wrong-secret-token");
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Forbidden, res2.StatusCode);
        var err2 = await res2.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts);
        Assert.NotNull(err2);
        Assert.Equal("ADMIN_KEY_INVALID", err2.Error.Code);

        // 3. Correct header -> 201 Created
        var req3 = new HttpRequestMessage(HttpMethod.Post, "/classes")
        {
            Content = JsonContent.Create(new CreateClassRequest("Secured Class 3", 30))
        };
        req3.Headers.Add("X-Admin-Key", configuredSecret);
        var res3 = await client.SendAsync(req3);
        Assert.Equal(HttpStatusCode.Created, res3.StatusCode);
        var cls = await res3.Content.ReadFromJsonAsync<ClassResponse>(JsonOpts);
        Assert.NotNull(cls);
        Assert.Equal("Secured Class 3", cls.Name);
    }
}
