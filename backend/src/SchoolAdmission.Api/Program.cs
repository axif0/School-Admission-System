using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Background;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Endpoints;
using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Api.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration ----
var connStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings__Default is required.");

// Add connection pool size
var connBuilder = new NpgsqlConnectionStringBuilder(connStr);
if (!connStr.Contains("Maximum Pool Size"))
    connBuilder.MaxPoolSize = 50;

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

// ---- Migrate DB ----
var logger = app.Services.GetRequiredService<ILogger<Program>>();
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
