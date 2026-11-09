using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data;
using Xunit;

namespace SchoolAdmission.Tests;

public class PostgresErrorClassifierTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public PostgresErrorClassifierTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static PostgresException CreatePgException(string sqlState, string message = "Non-English error: le verrou n'est pas disponible")
    {
        return new PostgresException(message, "ERROR", "ERROR", sqlState);
    }

    [Theory]
    [InlineData("55P03")] // LockNotAvailable
    [InlineData("53300")] // TooManyConnections
    [InlineData("40P01")] // DeadlockDetected
    [InlineData("40001")] // SerializationFailure
    public void IsBusy_KnownSqlStates_ReturnsTrue_RegardlessOfMessageLanguage(string sqlState)
    {
        var ex = CreatePgException(sqlState, "Une erreur s'est produite lors du verrouillage");
        Assert.True(PostgresErrorClassifier.IsBusy(ex));
    }

    [Fact]
    public void IsBusy_QueryCanceled_WhenNotClientAbort_ReturnsTrue()
    {
        var ex = CreatePgException(PostgresErrorCodes.QueryCanceled, "Délai d'attente de verrou dépassé");
        var cts = new CancellationTokenSource(); // Not cancelled
        Assert.True(PostgresErrorClassifier.IsBusy(ex, cts.Token));
    }

    [Fact]
    public void IsBusy_QueryCanceled_WhenClientAborted_ReturnsFalse()
    {
        var ex = CreatePgException(PostgresErrorCodes.QueryCanceled);
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Client aborted
        Assert.False(PostgresErrorClassifier.IsBusy(ex, cts.Token));
    }

    [Fact]
    public void IsBusy_TimeoutException_ReturnsTrue()
    {
        var ex = new TimeoutException("Operation timed out waiting for connection");
        Assert.True(PostgresErrorClassifier.IsBusy(ex));
    }

    [Fact]
    public void IsBusy_UnrelatedException_ReturnsFalse()
    {
        var ex = CreatePgException("23505", "unique_violation");
        Assert.False(PostgresErrorClassifier.IsBusy(ex));

        var invalidOp = new InvalidOperationException("Something else failed");
        Assert.False(PostgresErrorClassifier.IsBusy(invalidOp));
    }

    [Fact]
    public async Task LockTimeout_RealIntegration_Returns503TemporarilyBusy()
    {
        if (!_fixture.PostgresAvailable) return;

        using var factory = new SchoolAdmissionFactory(_fixture.ConnectionString);
        var client = factory.CreateClient();
        await client.GetAsync("/health/ready");

        // 1. Create a class
        var classRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Lock Timeout Test", 5));
        Assert.Equal(HttpStatusCode.Created, classRes.StatusCode);
        var cls = await classRes.Content.ReadFromJsonAsync<ClassResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var classId = cls!.Id;

        // 2. Open an external transaction and acquire exclusive row lock on the class
        await using var extConn = new NpgsqlConnection(_fixture.ConnectionString);
        await extConn.OpenAsync();
        await using var extTx = await extConn.BeginTransactionAsync();
        await using var lockCmd = new NpgsqlCommand("SELECT total_seats FROM classes WHERE id = @id FOR UPDATE", extConn, extTx);
        lockCmd.Parameters.AddWithValue("id", classId);
        await lockCmd.ExecuteScalarAsync();

        try
        {
            // 3. Fire hold request with short 1s lock timeout
            var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            request.Headers.Add("X-Student-Id", "20158888888888888");

            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            var err = await response.Content.ReadFromJsonAsync<ErrorResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.Equal("TEMPORARILY_BUSY", err!.Error.Code);
        }
        finally
        {
            await extTx.RollbackAsync();
        }
    }
}
