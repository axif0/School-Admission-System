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
    public static volatile bool IsReady = false;

    public static async Task MigrateAsync(IServiceProvider services, ILogger logger)
    {
        var dbOptions = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var maxAttempts = dbOptions.StartupRetries;
        var delay = TimeSpan.FromSeconds(dbOptions.StartupRetryDelaySeconds);

        // Retry until Postgres is ready
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

                    // Create partial unique index and performance indexes
                    await using var idxCmd = new NpgsqlCommand(@"
                        CREATE UNIQUE INDEX IF NOT EXISTS ix_holds_unique_active
                        ON holds (class_id, student_id)
                        WHERE status IN ('HELD','CONFIRMED');

                        CREATE INDEX IF NOT EXISTS ix_holds_sweeper_expiry
                        ON holds (expires_at)
                        WHERE status = 'HELD';

                        CREATE INDEX IF NOT EXISTS ix_holds_active_covering
                        ON holds (class_id, status, expires_at)
                        WHERE status IN ('HELD','CONFIRMED');

                        CREATE INDEX IF NOT EXISTS ix_classes_created_at
                        ON classes (created_at ASC);", conn);
                    await idxCmd.ExecuteNonQueryAsync();

                    logger.LogInformation("Migrations complete.");
                    IsReady = true;
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
            catch (NpgsqlException ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("DB not ready (attempt {Attempt}/{Max}): {Message}. Retrying in {Delay}s...", attempt, maxAttempts, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }

        throw new InvalidOperationException($"Could not connect to the database after {maxAttempts} attempts.");
    }
}
