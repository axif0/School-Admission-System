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
            HoldService svc) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request);
            var result = await svc.CreateHoldAsync(classId, studentId);
            return Results.Created($"/holds/{result.HoldId}", result);
        });

        // GET /holds/{holdId}
        app.MapGet("/holds/{holdId}", async (
            string holdId,
            HoldService svc) =>
        {
            var result = await svc.GetHoldAsync(holdId);
            return Results.Ok(result);
        });

        // POST /holds/{holdId}/confirm
        app.MapPost("/holds/{holdId}/confirm", async (
            string holdId,
            HttpContext ctx,
            HoldService svc) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request);
            var result = await svc.ConfirmHoldAsync(holdId, studentId);
            return Results.Ok(result);
        });

        // DELETE /holds/{holdId}
        app.MapDelete("/holds/{holdId}", async (
            string holdId,
            HttpContext ctx,
            HoldService svc) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request);
            await svc.CancelHoldAsync(holdId, studentId);
            return Results.NoContent();
        });

        // GET /me/holds
        app.MapGet("/me/holds", async (
            HttpContext ctx,
            HoldService svc) =>
        {
            var studentId = StudentIdValidation.ValidateAndGet(ctx.Request);
            var result = await svc.GetMyHoldsAsync(studentId);
            return Results.Ok(result);
        });
    }
}
