using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Endpoints;

public static class ClassEndpoints
{
    public static void MapClassEndpoints(this WebApplication app)
    {
        app.MapPost("/classes", async (
            HttpContext ctx,
            [FromBody] CreateClassRequest? req,
            IClassService svc,
            IOptions<AdminOptions> adminOptions,
            CancellationToken ct) =>
        {
            var adminKey = adminOptions.Value.ApiKey;
            if (!string.IsNullOrEmpty(adminKey))
            {
                if (!ctx.Request.Headers.TryGetValue("X-Admin-Key", out var headerKey) || string.IsNullOrWhiteSpace(headerKey))
                {
                    throw AppException.AdminKeyRequired();
                }

                var keyBytes = Encoding.UTF8.GetBytes(adminKey);
                var headerBytes = Encoding.UTF8.GetBytes(headerKey.ToString());

                if (keyBytes.Length != headerBytes.Length || !CryptographicOperations.FixedTimeEquals(keyBytes, headerBytes))
                {
                    throw AppException.AdminKeyInvalid();
                }
            }

            if (req is null)
                throw AppException.ValidationError("Request body is required.");
            var result = await svc.CreateAsync(req, ct);
            return Results.Created($"/classes/{result.Id}", result);
        });

        app.MapGet("/classes", async (IClassService svc, CancellationToken ct) =>
        {
            var jsonBytes = await svc.GetAllUtf8JsonAsync(ct);
            return Results.Bytes(jsonBytes, "application/json; charset=utf-8");
        });

        app.MapGet("/classes/stream", async (
            HttpContext ctx,
            ClassQueryCoalescer coalescer,
            CancellationToken ct) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            var (subId, reader) = coalescer.SubscribeUpdates();
            try
            {
                await ctx.Response.WriteAsync(": connected\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);

                while (!ct.IsCancellationRequested)
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
                    try
                    {
                        var targetClassId = await reader.ReadAsync(timeoutCts.Token);
                        await ctx.Response.WriteAsync($"event: classes-updated\ndata: {{\"classId\":\"{targetClassId}\"}}\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await ctx.Response.WriteAsync(": heartbeat\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal client disconnect
            }
            finally
            {
                coalescer.UnsubscribeUpdates(subId);
            }
        });

        app.MapGet("/classes/{classId}", async (string classId, IClassService svc, CancellationToken ct) =>
        {
            var cls = await svc.GetByIdAsync(classId, ct);
            return Results.Ok(cls);
        });
    }
}
