using System.ComponentModel.DataAnnotations;

namespace SchoolAdmission.Api.Configuration;

public class HoldOptions
{
    public const string SectionName = "Hold";

    [Range(1, 86400, ErrorMessage = "HOLD_DURATION_SECONDS must be between 1 and 86400 seconds.")]
    public int HoldDurationSeconds { get; set; } = 600;
}

public class DatabaseOptions
{
    public const string SectionName = "Database";

    [Range(1, 1000, ErrorMessage = "DB_MAX_POOL_SIZE must be greater than 0 and <= 1000.")]
    public int MaxPoolSize { get; set; } = 50;

    [Range(1, 300, ErrorMessage = "DB_LOCK_TIMEOUT_SECONDS must be between 1 and 300 seconds.")]
    public int LockTimeoutSeconds { get; set; } = 15;

    public long MigrationAdvisoryLockKey { get; set; } = 123456789L;

    [Range(1, 10000, ErrorMessage = "DB_MAX_CONNECTIONS must be greater than 0.")]
    public int MaxConnections { get; set; } = 200;

    [Range(1, 100, ErrorMessage = "EXPECTED_REPLICAS must be at least 1.")]
    public int ExpectedReplicas { get; set; } = 1;

    public int ReservedSuperuserConnections { get; set; } = 3;
}

public class SweeperOptions
{
    public const string SectionName = "Sweeper";

    public bool Enabled { get; set; } = true;

    [Range(1, 3600, ErrorMessage = "SWEEPER_INTERVAL_SECONDS must be between 1 and 3600 seconds.")]
    public int IntervalSeconds { get; set; } = 5;

    [Range(1, 10000, ErrorMessage = "SWEEPER_BATCH_SIZE must be between 1 and 10000.")]
    public int BatchSize { get; set; } = 500;

    public long AdvisoryLockKey { get; set; } = 987654321L;
}

public class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public bool Enabled { get; set; } = false;

    [Range(1, 10000)]
    public int HoldsPerMinutePerStudent { get; set; } = 10;

    [Range(1, 100000)]
    public int RequestsPerMinutePerIp { get; set; } = 60;
}

public class AdminOptions
{
    public const string SectionName = "Admin";

    public string? ApiKey { get; set; } = null;
}

public class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public string LogFormat { get; set; } = "text";

    public bool MetricsEnabled { get; set; } = true;

    public bool? SwaggerEnabled { get; set; } = null;
}
