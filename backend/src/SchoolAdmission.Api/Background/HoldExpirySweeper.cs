using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;

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

    public HoldExpirySweeper(
        IServiceScopeFactory scopeFactory,
        IOptions<SweeperOptions> options,
        ILogger<HoldExpirySweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
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
            await using var cmd = new NpgsqlCommand(
                "UPDATE holds SET status='EXPIRED' WHERE status='HELD' AND expires_at <= clock_timestamp()", conn);
            var rows = await cmd.ExecuteNonQueryAsync(ct);
            if (rows > 0)
                _logger.LogInformation("Sweeper expired {Count} holds", rows);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
