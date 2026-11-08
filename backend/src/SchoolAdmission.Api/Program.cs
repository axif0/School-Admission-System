using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SchoolAdmission.Api.Background;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Endpoints;
using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Api.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ---- Strongly-Typed Options ----
builder.Services.AddOptions<HoldOptions>()
    .Configure<IConfiguration>((opt, config) =>
    {
        config.GetSection(HoldOptions.SectionName).Bind(opt);
        if (int.TryParse(config["HOLD_DURATION_SECONDS"], out var sec))
            opt.HoldDurationSeconds = sec;
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<DatabaseOptions>()
    .Configure<IConfiguration>((opt, config) =>
    {
        config.GetSection(DatabaseOptions.SectionName).Bind(opt);
        if (int.TryParse(config["DB_MAX_POOL_SIZE"], out var pool))
            opt.MaxPoolSize = pool;
        if (int.TryParse(config["DB_LOCK_TIMEOUT_SECONDS"], out var timeout))
            opt.LockTimeoutSeconds = timeout;
        if (long.TryParse(config["MIGRATION_ADVISORY_LOCK_KEY"], out var key))
            opt.MigrationAdvisoryLockKey = key;
        if (int.TryParse(config["DB_MAX_CONNECTIONS"], out var maxConn))
            opt.MaxConnections = maxConn;
        if (int.TryParse(config["EXPECTED_REPLICAS"], out var replicas))
            opt.ExpectedReplicas = replicas;
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SweeperOptions>()
    .Configure<IConfiguration>((opt, config) =>
    {
        config.GetSection(SweeperOptions.SectionName).Bind(opt);
        if (int.TryParse(config["SWEEPER_INTERVAL_SECONDS"], out var interval))
            opt.IntervalSeconds = interval;
        if (bool.TryParse(config["SWEEPER_ENABLED"], out var enabled))
            opt.Enabled = enabled;
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<RateLimitOptions>()
    .Configure<IConfiguration>((opt, config) =>
    {
        config.GetSection(RateLimitOptions.SectionName).Bind(opt);
        if (bool.TryParse(config["RATE_LIMIT_ENABLED"], out var enabled))
            opt.Enabled = enabled;
        if (int.TryParse(config["RATE_LIMIT_HOLDS_PER_MINUTE_PER_STUDENT"], out var studentRate))
            opt.HoldsPerMinutePerStudent = studentRate;
        if (int.TryParse(config["RATE_LIMIT_PER_MINUTE_PER_IP"], out var ipRate))
            opt.RequestsPerMinutePerIp = ipRate;
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ObservabilityOptions>()
    .Configure<IConfiguration>((opt, config) =>
    {
        config.GetSection(ObservabilityOptions.SectionName).Bind(opt);
        if (bool.TryParse(config["METRICS_ENABLED"], out var metrics))
            opt.MetricsEnabled = metrics;
        if (bool.TryParse(config["SWAGGER_ENABLED"], out var swagger))
            opt.SwaggerEnabled = swagger;
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ---- Configuration ----
var connStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings__Default is required.");

// Add connection pool size
var connBuilder = new NpgsqlConnectionStringBuilder(connStr);
if (!connStr.Contains("Maximum Pool Size"))
{
    var poolSize = int.TryParse(builder.Configuration["DB_MAX_POOL_SIZE"], out var p) ? p : 50;
    connBuilder.MaxPoolSize = poolSize;
}

// ---- Services ----
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(connBuilder.ConnectionString));

builder.Services.AddScoped<ClassService>();
builder.Services.AddScoped<HoldService>();
builder.Services.AddSingleton<IdGenerator>();
builder.Services.AddHostedService<HoldExpirySweeper>();

// CORS
var corsOrigins = builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? "http://localhost:3000";
builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(corsOrigins.Split(',', StringSplitOptions.TrimEntries))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// JSON: camelCase + no enum conversion needed (we use string constants)
builder.Services.ConfigureHttpJsonOptions(opt =>
{
    opt.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opt.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Allow empty body / missing Content-Type on POST
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(opt =>
{
    opt.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// Swagger (dev only)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---- Middleware ----
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ---- Connection Budget & Migrate DB ----
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var dbOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
var totalRequired = dbOptions.MaxPoolSize * dbOptions.ExpectedReplicas;
var safeCapacity = dbOptions.MaxConnections - dbOptions.ReservedSuperuserConnections;

logger.LogInformation(
    "Connection Budget: {Replicas} replicas × {PoolSize} pool = {TotalRequired} (PostgreSQL capacity: {MaxConnections}, reserved: {Reserved})",
    dbOptions.ExpectedReplicas, dbOptions.MaxPoolSize, totalRequired, dbOptions.MaxConnections, dbOptions.ReservedSuperuserConnections);

if (totalRequired > safeCapacity)
{
    logger.LogWarning(
        "WARNING: Connection budget exceeds safe capacity! {TotalRequired} > {SafeCapacity}. Consider connection pooler (e.g. PgBouncer) or adjust DB_MAX_POOL_SIZE.",
        totalRequired, safeCapacity);
}

await DatabaseMigrator.MigrateAsync(app.Services, logger);

// ---- Endpoints ----
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

app.MapGet("/health/ready", async (AppDbContext db) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "unhealthy", error = ex.Message },
            statusCode: 503);
    }
});

app.MapGet("/health", async (AppDbContext db) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        return Results.Ok(new { status = "ok" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "unhealthy", error = ex.Message },
            statusCode: 503);
    }
});

app.MapClassEndpoints();
app.MapHoldEndpoints();

app.Run();

// Make Program visible for WebApplicationFactory in tests
public partial class Program { }
