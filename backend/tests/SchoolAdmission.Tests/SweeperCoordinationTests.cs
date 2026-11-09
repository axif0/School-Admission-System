using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using SchoolAdmission.Api.Contracts;
using Xunit;

namespace SchoolAdmission.Tests;

public class SweeperCoordinationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public SweeperCoordinationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TwoSweepers_RunningConcurrently_SweepsAllStaleHolds_WithoutDeadlocks()
    {
        if (!_fixture.PostgresAvailable) return;

        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();
        await client.GetAsync("/health/ready");

        // 1. Create a class
        var classRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Sweeper Multi Test", 1000));
        var cls = await classRes.Content.ReadFromJsonAsync<ClassResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var classId = cls!.Id;

        // 2. Insert 200 expired holds directly via SQL
        await using (var seedConn = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await seedConn.OpenAsync();
            var batchInsert = @"
                INSERT INTO holds (id, class_id, student_id, status, expires_at, created_at)
                SELECT
                    'h_stale_' || i,
                    @classId,
                    '2015000000' || lpad(i::text, 7, '0'),
                    'HELD',
                    now() - interval '5 seconds',
                    now() - interval '10 seconds'
                FROM generate_series(1, 200) AS i;";
            await using var cmd = new NpgsqlCommand(batchInsert, seedConn);
            cmd.Parameters.AddWithValue("classId", classId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Define sweep worker that acquires xact advisory lock and sweeps in batches of 50
        async Task<int> RunSweeperWorkerAsync(int workerId)
        {
            int totalSwept = 0;
            const long lockKey = 987654321L;
            const int batchSize = 50;

            for (int tick = 0; tick < 10; tick++)
            {
                await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
                await conn.OpenAsync();
                await using var tx = await conn.BeginTransactionAsync();

                await using var lockCmd = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key)", conn, tx);
                lockCmd.Parameters.AddWithValue("key", lockKey);
                var acquired = (bool)(await lockCmd.ExecuteScalarAsync() ?? false);

                if (!acquired)
                {
                    await tx.RollbackAsync();
                    await Task.Delay(10);
                    continue;
                }

                await using var sweepCmd = new NpgsqlCommand(@"
                    WITH batch AS (
                        SELECT id FROM holds
                        WHERE status = 'HELD' AND expires_at <= clock_timestamp()
                        ORDER BY expires_at
                        LIMIT @batchSize
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE holds h
                    SET status = 'EXPIRED'
                    FROM batch
                    WHERE h.id = batch.id", conn, tx);
                sweepCmd.Parameters.AddWithValue("batchSize", batchSize);

                var rows = await sweepCmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();

                totalSwept += rows;
                if (rows == 0) break;
                await Task.Delay(5);
            }

            return totalSwept;
        }

        // 4. Run two sweeper workers concurrently
        var t1 = Task.Run(() => RunSweeperWorkerAsync(1));
        var t2 = Task.Run(() => RunSweeperWorkerAsync(2));

        var results = await Task.WhenAll(t1, t2);
        var totalFlipped = results.Sum();

        Assert.Equal(200, totalFlipped);

        // Verify no HELD stale holds remain
        await using var verifyConn = new NpgsqlConnection(_fixture.ConnectionString);
        await verifyConn.OpenAsync();
        await using var countCmd = new NpgsqlCommand(
            "SELECT count(*) FROM holds WHERE class_id=@classId AND status='HELD'", verifyConn);
        countCmd.Parameters.AddWithValue("classId", classId);
        var remainingHeld = (long)(await countCmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(0, remainingHeld);
    }

    [Fact]
    public async Task ExpiryBoundaryRace_ConfirmsAroundExpiry_NeverConfirmsAfterExpiry()
    {
        if (!_fixture.PostgresAvailable) return;

        // 1-second hold duration
        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString, holdDurationSeconds: 1);
        var client = factory.CreateClient();
        await client.GetAsync("/health/ready");

        var classRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Expiry Race Class", 15));
        var cls = await classRes.Content.ReadFromJsonAsync<ClassResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var classId = cls!.Id;

        // Create 10 holds
        var holdIds = new List<(string HoldId, string StudentId)>();
        for (int i = 1; i <= 10; i++)
        {
            var studentId = $"2026000000000{i:D4}";
            var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            req.Headers.Add("X-Student-Id", studentId);
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            var hold = await res.Content.ReadFromJsonAsync<CreateHoldResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            holdIds.Add((hold!.HoldId, studentId));
        }

        // Sleep right up to expiry boundary (900ms)
        await Task.Delay(900);

        // Concurrently attempt to confirm all holds around the 1.0s expiry point
        var confirmTasks = holdIds.Select(async item =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/holds/{item.HoldId}/confirm");
            req.Headers.Add("X-Student-Id", item.StudentId);
            return await client.SendAsync(req);
        });

        var responses = await Task.WhenAll(confirmTasks);

        // Every response must be either 200 OK or 409 Conflict
        foreach (var r in responses)
        {
            Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                $"Unexpected status code: {r.StatusCode}");
        }

        // Mathematical invariant: NEVER confirm after expiry
        await using var verifyConn = new NpgsqlConnection(_fixture.ConnectionString);
        await verifyConn.OpenAsync();
        await using var invalidConfirmsCmd = new NpgsqlCommand(
            "SELECT count(*) FROM holds WHERE class_id=@classId AND status='CONFIRMED' AND confirmed_at > expires_at",
            verifyConn);
        invalidConfirmsCmd.Parameters.AddWithValue("classId", classId);
        var invalidConfirms = (long)(await invalidConfirmsCmd.ExecuteScalarAsync() ?? 0);
        Assert.Equal(0, invalidConfirms);
    }
}
