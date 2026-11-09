using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SchoolAdmission.Api.Background;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Data.Abstractions;
using SchoolAdmission.Api.Endpoints;
using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Api.Observability;
using SchoolAdmission.Api.Services;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging Configuration ----
var logFormat = builder.Configuration["LOG_FORMAT"] ?? "text";
builder.Logging.ClearProviders();
if (logFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
{
    builder.Logging.AddJsonConsole(opt =>
    {
        opt.IncludeScopes = true;
        opt.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        opt.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
    });
}
else
{
    builder.Logging.AddSimpleConsole(opt =>
    {
        opt.IncludeScopes = true;
        opt.SingleLine = true;
        opt.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    });
}

// ---- Strongly-Typed Options ----
builder.Services.AddOptions<HoldOptions>()
    .Configure<IConfiguration>((opt, config) =>
    {
        config.GetSection(HoldOptions.SectionName).Bind(opt);
        if (int.TryParse(config["HOLD_DURATION_SECONDS"], out var sec))
            opt.HoldDurationSeconds = sec;
        if (int.TryParse(config["MAX_CONCURRENT_WRITES_PER_CLASS"], out var maxWrites))
            opt.MaxConcurrentWritesPerClass = maxWrites;
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
        if (int.TryParse(config["DB_STARTUP_RETRIES"], out var retries))
            opt.StartupRetries = retries;
        if (int.TryParse(config["DB_STARTUP_RETRY_DELAY_SECONDS"], out var retryDelay))
            opt.StartupRetryDelaySeconds = retryDelay;
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SweeperOptions>()
    .Configure<IConfiguration>((opt, config) =>
    {
        config.GetSection(SweeperOptions.SectionName).Bind(opt);
        if (int.TryParse(config["SWEEPER_INTERVAL_SECONDS"], out var interval))
            opt.IntervalSeconds = interval;
        if (int.TryParse(config["SWEEPER_BATCH_SIZE"], out var batchSize))
            opt.BatchSize = batchSize;
        if (long.TryParse(config["SWEEPER_ADVISORY_LOCK_KEY"], out var lockKey))
            opt.AdvisoryLockKey = lockKey;
        if (bool.TryParse(config["SWEEPER_ENABLED"], out var enabled))
            opt.Enabled = enabled;
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AdminOptions>()
    .Configure<IConfiguration>((opt, config) =>
    {
        config.GetSection(AdminOptions.SectionName).Bind(opt);
        if (config["ADMIN_API_KEY"] is { } key && !string.IsNullOrWhiteSpace(key))
            opt.ApiKey = key;
        if (config["STUDENT_AUTH_SECRET"] is { } studentSecret && !string.IsNullOrWhiteSpace(studentSecret))
            opt.StudentAuthSecret = studentSecret;
    });

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
        if (config["LOG_FORMAT"] is { } lf)
            opt.LogFormat = lf;
        if (bool.TryParse(config["METRICS_ENABLED"], out var metrics))
            opt.MetricsEnabled = metrics;
        if (bool.TryParse(config["SWAGGER_ENABLED"], out var swagger))
            opt.SwaggerEnabled = swagger;
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

var connStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings__Default is required.");

var connBuilder = new NpgsqlConnectionStringBuilder(connStr);
if (!connStr.Contains("Maximum Pool Size"))
{
    var poolSize = int.TryParse(builder.Configuration["DB_MAX_POOL_SIZE"], out var p) ? p : 50;
    connBuilder.MaxPoolSize = poolSize;
}

// ---- Rate Limiter (Built-in, Configurable) ----
builder.Services.AddRateLimiter(opt =>
{
    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opt.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        context.HttpContext.Response.Headers.RetryAfter = "60";
        var body = new ErrorResponse(new ErrorDetail("TOO_MANY_REQUESTS", "Rate limit exceeded. Please wait before retrying.", context.HttpContext.TraceIdentifier));
        await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }), token);
    };

    opt.AddPolicy("HoldRateLimit", httpContext =>
    {
        var rateOpts = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        if (!rateOpts.Enabled)
        {
            return RateLimitPartition.GetNoLimiter("unlimited");
        }

        var studentId = httpContext.Request.Headers["X-Student-Id"].FirstOrDefault()?.Trim();
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var partitionKey = !string.IsNullOrEmpty(studentId) ? $"student:{studentId}" : $"ip:{ip}";

        var limit = !string.IsNullOrEmpty(studentId)
            ? rateOpts.HoldsPerMinutePerStudent
            : rateOpts.RequestsPerMinutePerIp;

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// ---- Services ----
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(connBuilder.ConnectionString));

builder.Services.AddSingleton<ClassQueryCoalescer>();
builder.Services.AddSingleton<ClassWriteGate>();
builder.Services.AddSingleton<AppMetrics>();
builder.Services.AddScoped<IClassStore, PostgresClassStore>();
builder.Services.AddScoped<IHoldStore, PostgresHoldStore>();
builder.Services.AddScoped<IClassService, ClassService>();
builder.Services.AddScoped<IHoldService, HoldService>();
builder.Services.AddSingleton<IdGenerator>();
builder.Services.AddHostedService<HoldExpirySweeper>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

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

builder.Services.ConfigureHttpJsonOptions(opt =>
{
    opt.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opt.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---- Host & Kestrel Hardening ----
builder.Services.Configure<HostOptions>(opts =>
{
    opts.ShutdownTimeout = TimeSpan.FromSeconds(25);
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;
    serverOptions.Limits.MaxRequestBodySize = 8 * 1024; // 8 KB
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
});

var app = builder.Build();

// ---- Middleware ----
app.UseForwardedHeaders();
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors();
app.UseRateLimiter();

var obsOptions = app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
var isSwaggerActive = obsOptions.SwaggerEnabled ?? app.Environment.IsDevelopment();
if (isSwaggerActive)
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

app.MapGet("/metrics", (HttpContext ctx, IOptions<ObservabilityOptions> obsOptions, AppMetrics metrics) =>
{
    if (!obsOptions.Value.MetricsEnabled)
        return Results.NotFound();

    var accept = ctx.Request.Headers.Accept.ToString();
    var useOpenMetrics = accept.Contains("application/openmetrics-text", StringComparison.OrdinalIgnoreCase);
    var contentType = useOpenMetrics
        ? "application/openmetrics-text; version=1.0.0; charset=utf-8"
        : "text/plain; version=0.0.4; charset=utf-8";

    return Results.Content(metrics.ToPrometheusFormat(useOpenMetrics), contentType);
});

app.MapGet("/health/ready", async (AppDbContext db) =>
{
    if (!DatabaseMigrator.IsReady)
        return Results.Json(new { status = "unhealthy", error = "Database migrations in progress" },
            statusCode: 503);

    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Readiness check failed");
        return Results.Json(new { status = "unhealthy", error = "Database check failed" },
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
        logger.LogWarning(ex, "Health check failed");
        return Results.Json(new { status = "unhealthy", error = "Database check failed" },
            statusCode: 503);
    }
});

app.MapClassEndpoints();
app.MapHoldEndpoints();

app.Run();

// Make Program visible for WebApplicationFactory in tests
public partial class Program { }
