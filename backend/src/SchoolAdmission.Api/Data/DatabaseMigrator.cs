using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SchoolAdmission.Api.Configuration;

namespace SchoolAdmission.Api.Data;

/// <summary>
/// Runs EF Core migrations at startup using a Postgres advisory lock
/// to prevent concurrent migration conflicts when multiple processes start.
/// </summary>
public static class DatabaseMigrator
{
    public static async Task MigrateAsync(IServiceProvider services, ILogger logger)
    {
        // Retry until Postgres is ready
        for (int attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var dbOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                var advisoryKey = dbOptions.MigrationAdvisoryLockKey;

                var conn = (NpgsqlConnection)db.Database.GetDbConnection();
                await conn.OpenAsync();

                // Acquire advisory lock (blocking)
                await using (var lockCmd = new NpgsqlCommand(
                    $"SELECT pg_advisory_lock({advisoryKey})", conn))
                    await lockCmd.ExecuteNonQueryAsync();

                try
                {
                    logger.LogInformation("Running EF Core migrations...");
                    await db.Database.MigrateAsync();

                    // Create partial unique index (EF Core doesn't support partial indexes via HasFilter in all scenarios)
                    await using var idxCmd = new NpgsqlCommand(@"
                        CREATE UNIQUE INDEX IF NOT EXISTS ix_holds_unique_active
                        ON holds (class_id, student_id)
                        WHERE status IN ('HELD','CONFIRMED')", conn);
                    await idxCmd.ExecuteNonQueryAsync();

                    logger.LogInformation("Migrations complete.");
                }
                finally
                {
                    await using (var unlockCmd = new NpgsqlCommand(
                        $"SELECT pg_advisory_unlock({advisoryKey})", conn))
                        await unlockCmd.ExecuteNonQueryAsync();
                    await conn.CloseAsync();
                }

                return; // success
            }
            catch (NpgsqlException ex) when (attempt < 30)
            {
                logger.LogWarning("DB not ready (attempt {Attempt}): {Message}. Retrying in 2s...", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new InvalidOperationException("Could not connect to the database after 30 attempts.");
    }
}
