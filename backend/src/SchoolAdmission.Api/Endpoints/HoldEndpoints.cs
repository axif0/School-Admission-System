using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Endpoints;

public static class HoldEndpoints
{
    public static void MapHoldEndpoints(this WebApplication app)
    {
        // POST /classes/{classId}/holds
        app.MapPost("/classes/{classId}/holds", async (
            string classId,
            HttpContext ctx,
            IHoldService svc,
            CancellationToken ct) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request);
            var result = await svc.CreateHoldAsync(classId, studentId, ct);
            return Results.Created($"/holds/{result.HoldId}", result);
        }).RequireRateLimiting("HoldRateLimit");

        // GET /holds/{holdId}
        app.MapGet("/holds/{holdId}", async (
            string holdId,
            IHoldService svc,
            CancellationToken ct) =>
        {
            var result = await svc.GetHoldAsync(holdId, ct);
            return Results.Ok(result);
        });

        // POST /holds/{holdId}/confirm
        app.MapPost("/holds/{holdId}/confirm", async (
            string holdId,
            HttpContext ctx,
            IHoldService svc,
            CancellationToken ct) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request);
            var result = await svc.ConfirmHoldAsync(holdId, studentId, ct);
            return Results.Ok(result);
        }).RequireRateLimiting("HoldRateLimit");

        // DELETE /holds/{holdId}
        app.MapDelete("/holds/{holdId}", async (
            string holdId,
            HttpContext ctx,
            IHoldService svc,
            CancellationToken ct) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request);
            await svc.CancelHoldAsync(holdId, studentId, ct);
            return Results.NoContent();
        });

        // GET /me/holds
        app.MapGet("/me/holds", async (
            HttpContext ctx,
            IHoldService svc,
            CancellationToken ct) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request);
            var result = await svc.GetMyHoldsAsync(studentId, ct);
            return Results.Ok(result);
        });
    }
}
