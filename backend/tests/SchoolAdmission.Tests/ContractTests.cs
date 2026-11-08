using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SchoolAdmission.Tests;

public class ContractTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ContractTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Section6_AllEndpoints_AdhereToExactContract()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();

        // 1. POST /classes -> 201 { id, name, totalSeats }
        var createClassRes = await client.PostAsJsonAsync("/classes", new { name = "Class 6 - Morning", totalSeats = 2 });
        Assert.Equal(HttpStatusCode.Created, createClassRes.StatusCode);
        var createClassJson = await createClassRes.Content.ReadAsStringAsync();
        using var classDoc = JsonDocument.Parse(createClassJson);
        var classRoot = classDoc.RootElement;

        Assert.True(classRoot.TryGetProperty("id", out var classIdEl));
        Assert.True(classRoot.TryGetProperty("name", out var classNameEl));
        Assert.True(classRoot.TryGetProperty("totalSeats", out var totalSeatsEl));
        Assert.StartsWith("c_", classIdEl.GetString()!);
        Assert.Equal("Class 6 - Morning", classNameEl.GetString());
        Assert.Equal(2, totalSeatsEl.GetInt32());

        var classId = classIdEl.GetString()!;

        // 2. GET /classes -> 200 array of { id, name, totalSeats, heldSeats, confirmedSeats, availableSeats }
        var listRes = await client.GetAsync("/classes");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var listJson = await listRes.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listJson);
        Assert.Equal(JsonValueKind.Array, listDoc.RootElement.ValueKind);
        var listedClass = listDoc.RootElement.EnumerateArray().First(x => x.GetProperty("id").GetString() == classId);
        Assert.True(listedClass.TryGetProperty("id", out _));
        Assert.True(listedClass.TryGetProperty("name", out _));
        Assert.True(listedClass.TryGetProperty("totalSeats", out _));
        Assert.True(listedClass.TryGetProperty("heldSeats", out _));
        Assert.True(listedClass.TryGetProperty("confirmedSeats", out _));
        Assert.True(listedClass.TryGetProperty("availableSeats", out _));

        // 3. GET /classes/{id} -> 200 { id, name, totalSeats, heldSeats, confirmedSeats, availableSeats }
        var getClsRes = await client.GetAsync($"/classes/{classId}");
        Assert.Equal(HttpStatusCode.OK, getClsRes.StatusCode);
        var getClsJson = await getClsRes.Content.ReadAsStringAsync();
        using var getClsDoc = JsonDocument.Parse(getClsJson);
        Assert.True(getClsDoc.RootElement.TryGetProperty("id", out _));
        Assert.True(getClsDoc.RootElement.TryGetProperty("name", out _));
        Assert.True(getClsDoc.RootElement.TryGetProperty("totalSeats", out _));
        Assert.True(getClsDoc.RootElement.TryGetProperty("heldSeats", out _));
        Assert.True(getClsDoc.RootElement.TryGetProperty("confirmedSeats", out _));
        Assert.True(getClsDoc.RootElement.TryGetProperty("availableSeats", out _));

        // 4. POST /classes/{id}/holds -> 201 { holdId, classId, studentId, status, expiresAt, serverTime }
        var holdReq = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        holdReq.Headers.Add("X-Student-Id", "20150000000000001");
        var holdRes = await client.SendAsync(holdReq);
        Assert.Equal(HttpStatusCode.Created, holdRes.StatusCode);
        var holdJson = await holdRes.Content.ReadAsStringAsync();
        using var holdDoc = JsonDocument.Parse(holdJson);
        var holdRoot = holdDoc.RootElement;

        Assert.True(holdRoot.TryGetProperty("holdId", out var holdIdEl));
        Assert.True(holdRoot.TryGetProperty("classId", out var holdClassIdEl));
        Assert.True(holdRoot.TryGetProperty("studentId", out var holdStudentIdEl));
        Assert.True(holdRoot.TryGetProperty("status", out var holdStatusEl));
        Assert.True(holdRoot.TryGetProperty("expiresAt", out var expiresAtEl));
        Assert.True(holdRoot.TryGetProperty("serverTime", out var serverTimeEl));

        Assert.StartsWith("h_", holdIdEl.GetString()!);
        Assert.Equal(classId, holdClassIdEl.GetString());
        Assert.Equal("20150000000000001", holdStudentIdEl.GetString());
        Assert.Equal("HELD", holdStatusEl.GetString());
        Assert.EndsWith("Z", expiresAtEl.GetString()!);
        Assert.EndsWith("Z", serverTimeEl.GetString()!);

        var holdId = holdIdEl.GetString()!;

        // 5. GET /holds/{id} -> 200 { holdId, classId, status, expiresAt, serverTime }
        var getHoldRes = await client.GetAsync($"/holds/{holdId}");
        Assert.Equal(HttpStatusCode.OK, getHoldRes.StatusCode);
        var getHoldJson = await getHoldRes.Content.ReadAsStringAsync();
        using var getHoldDoc = JsonDocument.Parse(getHoldJson);
        var getHoldRoot = getHoldDoc.RootElement;
        Assert.True(getHoldRoot.TryGetProperty("holdId", out _));
        Assert.True(getHoldRoot.TryGetProperty("classId", out _));
        Assert.True(getHoldRoot.TryGetProperty("status", out var ghStatusEl));
        Assert.True(getHoldRoot.TryGetProperty("expiresAt", out var ghExpEl));
        Assert.True(getHoldRoot.TryGetProperty("serverTime", out var ghServEl));
        Assert.Equal("HELD", ghStatusEl.GetString());
        Assert.EndsWith("Z", ghExpEl.GetString()!);
        Assert.EndsWith("Z", ghServEl.GetString()!);

        // 6. POST /holds/{id}/confirm -> 200 { holdId, status: "CONFIRMED" }
        var confReq = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        confReq.Headers.Add("X-Student-Id", "20150000000000001");
        var confRes = await client.SendAsync(confReq);
        Assert.Equal(HttpStatusCode.OK, confRes.StatusCode);
        var confJson = await confRes.Content.ReadAsStringAsync();
        using var confDoc = JsonDocument.Parse(confJson);
        var confRoot = confDoc.RootElement;
        Assert.True(confRoot.TryGetProperty("holdId", out _));
        Assert.True(confRoot.TryGetProperty("status", out var confStatusEl));
        Assert.Equal("CONFIRMED", confStatusEl.GetString());

        // 7. DELETE /holds/{id} on second hold -> 204 No Content
        var holdReq2 = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        holdReq2.Headers.Add("X-Student-Id", "20150000000000002");
        var holdRes2 = await client.SendAsync(holdReq2);
        Assert.Equal(HttpStatusCode.Created, holdRes2.StatusCode);
        var holdJson2 = await holdRes2.Content.ReadAsStringAsync();
        using var holdDoc2 = JsonDocument.Parse(holdJson2);
        var holdId2 = holdDoc2.RootElement.GetProperty("holdId").GetString()!;

        var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdId2}");
        delReq.Headers.Add("X-Student-Id", "20150000000000002");
        var delRes = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // 8. GET /me/holds -> 200 array of { holdId, classId, className, status, expiresAt }
        var meReq = new HttpRequestMessage(HttpMethod.Get, "/me/holds");
        meReq.Headers.Add("X-Student-Id", "20150000000000001");
        var meRes = await client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);
        var meJson = await meRes.Content.ReadAsStringAsync();
        using var meDoc = JsonDocument.Parse(meJson);
        Assert.Equal(JsonValueKind.Array, meDoc.RootElement.ValueKind);
        var myItem = meDoc.RootElement.EnumerateArray().First();
        Assert.True(myItem.TryGetProperty("holdId", out _));
        Assert.True(myItem.TryGetProperty("classId", out _));
        Assert.True(myItem.TryGetProperty("className", out _));
        Assert.True(myItem.TryGetProperty("status", out var myStatusEl));
        Assert.True(myItem.TryGetProperty("expiresAt", out var myExpEl));
        Assert.Equal("CONFIRMED", myStatusEl.GetString());
        Assert.EndsWith("Z", myExpEl.GetString()!);

        // Error Response Shape Test -> 404
        var errRes = await client.GetAsync("/classes/c_nonexistent999");
        Assert.Equal(HttpStatusCode.NotFound, errRes.StatusCode);
        var errJson = await errRes.Content.ReadAsStringAsync();
        using var errDoc = JsonDocument.Parse(errJson);
        Assert.True(errDoc.RootElement.TryGetProperty("error", out var errorEl));
        Assert.True(errorEl.TryGetProperty("code", out _));
        Assert.True(errorEl.TryGetProperty("message", out _));
        Assert.True(errorEl.TryGetProperty("traceId", out _));
    }
}
