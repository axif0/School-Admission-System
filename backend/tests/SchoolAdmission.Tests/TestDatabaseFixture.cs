using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Tests;

public class TestDatabaseFixture : IAsyncLifetime
{
    public string DatabaseName { get; } = $"school_admission_test_{Guid.NewGuid():N}";
    private string AdminConnectionString { get; set; } = "";
    public string ConnectionString { get; private set; } = "";
    public bool PostgresAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        AdminConnectionString = Environment.GetEnvironmentVariable("TEST_ADMIN_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

        try
        {
            await using var conn = new NpgsqlConnection(AdminConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\"", conn);
            await cmd.ExecuteNonQueryAsync();

            var cb = new NpgsqlConnectionStringBuilder(AdminConnectionString)
            {
                Database = DatabaseName
            };
            ConnectionString = cb.ConnectionString;
            PostgresAvailable = true;
        }
        catch (Exception)
        {
            PostgresAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (!PostgresAvailable) return;

        try
        {
            await using var conn = new NpgsqlConnection(AdminConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($@"
                SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{DatabaseName}';
                DROP DATABASE IF EXISTS ""{DatabaseName}"";", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

public class SchoolAdmissionFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly int _holdDurationSeconds;
    private readonly bool _rateLimitEnabled;
    private readonly int _rateLimitPerStudent;
    private readonly string? _adminApiKey;
    private readonly string? _studentAuthSecret;

    public SchoolAdmissionFactory(
        string connectionString,
        int holdDurationSeconds = 600,
        bool rateLimitEnabled = false,
        int rateLimitPerStudent = 10,
        string? adminApiKey = null,
        string? studentAuthSecret = null)
    {
        _connectionString = connectionString;
        _holdDurationSeconds = holdDurationSeconds;
        _rateLimitEnabled = rateLimitEnabled;
        _rateLimitPerStudent = rateLimitPerStudent;
        _adminApiKey = adminApiKey;
        _studentAuthSecret = studentAuthSecret;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            var inMem = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["HOLD_DURATION_SECONDS"] = _holdDurationSeconds.ToString(),
                ["CORS_ALLOWED_ORIGINS"] = "http://localhost:3000",
                ["RATE_LIMIT_ENABLED"] = _rateLimitEnabled.ToString(),
                ["RATE_LIMIT_HOLDS_PER_MINUTE_PER_STUDENT"] = _rateLimitPerStudent.ToString()
            };
            if (!string.IsNullOrEmpty(_adminApiKey))
            {
                inMem["ADMIN_API_KEY"] = _adminApiKey;
            }
            if (!string.IsNullOrEmpty(_studentAuthSecret))
            {
                inMem["STUDENT_AUTH_SECRET"] = _studentAuthSecret;
            }
            config.AddInMemoryCollection(inMem);
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(_connectionString));
        });
    }
}
