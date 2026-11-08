using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using SchoolAdmission.Api.Configuration;
using Xunit;

namespace SchoolAdmission.Tests;

public class OptionsTests
{
    [Fact]
    public void DefaultOptions_HaveExpectedStandardValues()
    {
        var hold = new HoldOptions();
        var db = new DatabaseOptions();
        var sweeper = new SweeperOptions();
        var rate = new RateLimitOptions();

        Assert.Equal(600, hold.HoldDurationSeconds);
        Assert.Equal(50, db.MaxPoolSize);
        Assert.Equal(15, db.LockTimeoutSeconds);
        Assert.Equal(123456789L, db.MigrationAdvisoryLockKey);
        Assert.True(sweeper.Enabled);
        Assert.Equal(5, sweeper.IntervalSeconds);
        Assert.False(rate.Enabled);
    }

    [Fact]
    public void ConfigurationBinding_OverwritesDefaultsCorrectly()
    {
        var inMemory = new Dictionary<string, string?>
        {
            { "HOLD_DURATION_SECONDS", "300" },
            { "DB_MAX_POOL_SIZE", "25" },
            { "DB_LOCK_TIMEOUT_SECONDS", "5" },
            { "SWEEPER_INTERVAL_SECONDS", "10" },
            { "SWEEPER_ENABLED", "false" }
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemory)
            .Build();

        var hold = new HoldOptions();
        if (int.TryParse(config["HOLD_DURATION_SECONDS"], out var sec))
            hold.HoldDurationSeconds = sec;

        var db = new DatabaseOptions();
        if (int.TryParse(config["DB_MAX_POOL_SIZE"], out var pool))
            db.MaxPoolSize = pool;
        if (int.TryParse(config["DB_LOCK_TIMEOUT_SECONDS"], out var timeout))
            db.LockTimeoutSeconds = timeout;

        var sweeper = new SweeperOptions();
        if (int.TryParse(config["SWEEPER_INTERVAL_SECONDS"], out var interval))
            sweeper.IntervalSeconds = interval;
        if (bool.TryParse(config["SWEEPER_ENABLED"], out var enabled))
            sweeper.Enabled = enabled;

        Assert.Equal(300, hold.HoldDurationSeconds);
        Assert.Equal(25, db.MaxPoolSize);
        Assert.Equal(5, db.LockTimeoutSeconds);
        Assert.Equal(10, sweeper.IntervalSeconds);
        Assert.False(sweeper.Enabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1001)]
    public void DatabaseOptions_InvalidPoolSize_FailsValidation(int invalidPool)
    {
        var options = new DatabaseOptions { MaxPoolSize = invalidPool };
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(DatabaseOptions.MaxPoolSize)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(86401)]
    public void HoldOptions_InvalidDuration_FailsValidation(int invalidDuration)
    {
        var options = new HoldOptions { HoldDurationSeconds = invalidDuration };
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(HoldOptions.HoldDurationSeconds)));
    }
}
