using Microsoft.Extensions.Options;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Endpoints;

public static class HoldEndpoints
{
    public static void MapHoldEndpoints(this WebApplication app)
    {
        app.MapPost("/classes/{classId}/holds", async (
            string classId,
            HttpContext ctx,
            IHoldService svc,
            IOptions<AdminOptions> adminOptions,
            CancellationToken ct) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request, adminOptions.Value.StudentAuthSecret);
            var result = await svc.CreateHoldAsync(classId, studentId, ct);
            return Results.Created($"/holds/{result.HoldId}", result);
        }).RequireRateLimiting("HoldRateLimit");

        app.MapGet("/holds/{holdId}", async (
            string holdId,
            IHoldService svc,
            CancellationToken ct) =>
        {
            var result = await svc.GetHoldAsync(holdId, ct);
            return Results.Ok(result);
        });

        app.MapPost("/holds/{holdId}/confirm", async (
            string holdId,
            HttpContext ctx,
            IHoldService svc,
            IOptions<AdminOptions> adminOptions,
            CancellationToken ct) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request, adminOptions.Value.StudentAuthSecret);
            var result = await svc.ConfirmHoldAsync(holdId, studentId, ct);
            return Results.Ok(result);
        }).RequireRateLimiting("HoldRateLimit");

        app.MapDelete("/holds/{holdId}", async (
            string holdId,
            HttpContext ctx,
            IHoldService svc,
            IOptions<AdminOptions> adminOptions,
            CancellationToken ct) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request, adminOptions.Value.StudentAuthSecret);
            await svc.CancelHoldAsync(holdId, studentId, ct);
            return Results.NoContent();
        });

        app.MapGet("/me/holds", async (
            HttpContext ctx,
            IHoldService svc,
            IOptions<AdminOptions> adminOptions,
            CancellationToken ct) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request, adminOptions.Value.StudentAuthSecret);
            var result = await svc.GetMyHoldsAsync(studentId, ct);
            return Results.Ok(result);
        });
    }
}
