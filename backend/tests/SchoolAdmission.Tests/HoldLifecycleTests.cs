using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolAdmission.Api.Contracts;

namespace SchoolAdmission.Tests;

public class HoldLifecycleTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public HoldLifecycleTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task ExpiryFreesSeat_AutomaticallyWithoutApiCall()
    {
        // Set hold duration to 1 second
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString, holdDurationSeconds: 1);
        var client = factory.CreateClient();

        // 1. Create class with 1 seat
        var createRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Expiry Test Class", 1));
        var cls = await createRes.Content.ReadFromJsonAsync<ClassResponse>(JsonOpts);
        var classId = cls!.Id;

        // 2. Student 1 holds the only seat
        var holdReq = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        holdReq.Headers.Add("X-Student-Id", "20152691234567891");
        var holdRes = await client.SendAsync(holdReq);
        Assert.Equal(HttpStatusCode.Created, holdRes.StatusCode);
        var hold = await holdRes.Content.ReadFromJsonAsync<CreateHoldResponse>(JsonOpts);
        var holdId = hold!.HoldId;

        // Verify available is 0
        var classDetail = await (await client.GetAsync($"/classes/{classId}")).Content.ReadFromJsonAsync<ClassDetailResponse>(JsonOpts);
        Assert.Equal(0, classDetail!.AvailableSeats);

        // 3. Wait 2 seconds for hold to expire (duration is 1s)
        await Task.Delay(2000);

        // 4. GET /holds/{id} must derived-report EXPIRED
        var getHoldRes = await client.GetAsync($"/holds/{holdId}");
        Assert.Equal(HttpStatusCode.OK, getHoldRes.StatusCode);
        var getHold = await getHoldRes.Content.ReadFromJsonAsync<HoldResponse>(JsonOpts);
        Assert.Equal("EXPIRED", getHold!.Status);

        // 5. Available seats must be restored to 1
        var classDetailAfter = await (await client.GetAsync($"/classes/{classId}")).Content.ReadFromJsonAsync<ClassDetailResponse>(JsonOpts);
        Assert.Equal(1, classDetailAfter!.AvailableSeats);
        Assert.Equal(0, classDetailAfter.HeldSeats);

        // 6. Student 2 can now hold the seat
        var holdReq2 = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        holdReq2.Headers.Add("X-Student-Id", "20152691234567892");
        var holdRes2 = await client.SendAsync(holdReq2);
        Assert.Equal(HttpStatusCode.Created, holdRes2.StatusCode);
    }

    [Fact]
    public async Task ConfirmRules_ValidHold_Idempotent_ExpiredAndForeignRejected()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString, holdDurationSeconds: 1);
        var client = factory.CreateClient();

        var createRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Confirm Test Class", 10));
        var cls = await createRes.Content.ReadFromJsonAsync<ClassResponse>(JsonOpts);
        var classId = cls!.Id;

        const string studentA = "20152691234567891";
        const string studentB = "20152691234567892";

        // Student A holds seat
        var holdReq = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        holdReq.Headers.Add("X-Student-Id", studentA);
        var holdRes = await client.SendAsync(holdReq);
        var hold = await holdRes.Content.ReadFromJsonAsync<CreateHoldResponse>(JsonOpts);
        var holdId = hold!.HoldId;

        // Foreign student cannot confirm -> 403 NOT_YOUR_HOLD
        var foreignConfirm = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        foreignConfirm.Headers.Add("X-Student-Id", studentB);
        var foreignRes = await client.SendAsync(foreignConfirm);
        Assert.Equal(HttpStatusCode.Forbidden, foreignRes.StatusCode);
        var foreignErr = await foreignRes.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts);
        Assert.Equal("NOT_YOUR_HOLD", foreignErr!.Error.Code);

        // Owner confirms -> 200 CONFIRMED
        var ownConfirm = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        ownConfirm.Headers.Add("X-Student-Id", studentA);
        var ownRes = await client.SendAsync(ownConfirm);
        Assert.Equal(HttpStatusCode.OK, ownRes.StatusCode);
        var confirmBody = await ownRes.Content.ReadFromJsonAsync<ConfirmHoldResponse>(JsonOpts);
        Assert.Equal("CONFIRMED", confirmBody!.Status);

        // Confirm again -> 200 CONFIRMED (idempotent, confirmed seats rises once)
        var ownConfirm2 = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        ownConfirm2.Headers.Add("X-Student-Id", studentA);
        var ownRes2 = await client.SendAsync(ownConfirm2);
        Assert.Equal(HttpStatusCode.OK, ownRes2.StatusCode);

        var classDetail = await (await client.GetAsync($"/classes/{classId}")).Content.ReadFromJsonAsync<ClassDetailResponse>(JsonOpts);
        Assert.Equal(1, classDetail!.ConfirmedSeats);
        Assert.Equal(0, classDetail.HeldSeats);
    }

    [Fact]
    public async Task CancelRules_HeldFreed_ConfirmedRejected_IdempotentOnCancelled()
    {
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();

        var createRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Cancel Test Class", 5));
        var cls = await createRes.Content.ReadFromJsonAsync<ClassResponse>(JsonOpts);
        var classId = cls!.Id;

        const string studentId = "20152691234567899";

        // Hold a seat
        var holdReq = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        holdReq.Headers.Add("X-Student-Id", studentId);
        var holdRes = await client.SendAsync(holdReq);
        var hold = await holdRes.Content.ReadFromJsonAsync<CreateHoldResponse>(JsonOpts);
        var holdId = hold!.HoldId;

        // Cancel -> 204 NoContent
        var cancelReq = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdId}");
        cancelReq.Headers.Add("X-Student-Id", studentId);
        var cancelRes = await client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.NoContent, cancelRes.StatusCode);

        // Check hold status -> CANCELLED
        var getHold = await (await client.GetAsync($"/holds/{holdId}")).Content.ReadFromJsonAsync<HoldResponse>(JsonOpts);
        Assert.Equal("CANCELLED", getHold!.Status);

        // Repeat cancel -> 204 (idempotent)
        var cancelReq2 = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdId}");
        cancelReq2.Headers.Add("X-Student-Id", studentId);
        var cancelRes2 = await client.SendAsync(cancelReq2);
        Assert.Equal(HttpStatusCode.NoContent, cancelRes2.StatusCode);
    }
}
