using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Observability;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Background;

/// <summary>
/// Optional background sweeper that periodically marks expired HELD holds as EXPIRED in the DB.
/// Correctness does NOT depend on this — derived expiry handles it. This is for tidiness only.
/// </summary>
public class HoldExpirySweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<SweeperOptions> _options;
    private readonly ILogger<HoldExpirySweeper> _logger;
    private readonly AppMetrics? _metrics;
    private readonly ClassQueryCoalescer? _coalescer;

    public HoldExpirySweeper(
        IServiceScopeFactory scopeFactory,
        IOptions<SweeperOptions> options,
        ILogger<HoldExpirySweeper> logger,
        AppMetrics? metrics = null,
        ClassQueryCoalescer? coalescer = null)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _metrics = metrics;
        _coalescer = coalescer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("HoldExpirySweeper is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(_options.Value.IntervalSeconds);
        _logger.LogInformation("HoldExpirySweeper started with interval {Interval}s.", _options.Value.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sweeper error");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            var lockKey = _options.Value.AdvisoryLockKey;
            var batchSize = _options.Value.BatchSize;

            while (!ct.IsCancellationRequested)
            {
                await using var tx = await conn.BeginTransactionAsync(ct);

                // Transaction-level advisory lock: automatically released on commit/rollback
                await using var lockCmd = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key)", conn, tx);
                lockCmd.Parameters.AddWithValue("key", lockKey);
                var acquired = (bool)(await lockCmd.ExecuteScalarAsync(ct) ?? false);

                if (!acquired)
                {
                    _logger.LogDebug("Sweeper skipped (another instance sweeping).");
                    await tx.RollbackAsync(ct);
                    return; // Another replica is actively sweeping this tick
                }

                // Batched update with SKIP LOCKED to never block active writers
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

                var rows = await sweepCmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);

                if (rows > 0)
                {
                    _logger.LogInformation("Sweeper expired {Count} holds in batch.", rows);
                    _metrics?.ExpiredSweptTotal.Add(rows);
                    _coalescer?.OnWriteOccurred();
                }

                if (rows < batchSize)
                    break;

                await Task.Delay(25, ct); // Yield briefly between batches
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
