using Microsoft.AspNetCore.Mvc;
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
            CancellationToken ct) =>
        {
            if (req is null)
                throw AppException.ValidationError("Request body is required.");
            var result = await svc.CreateAsync(req, ct);
            return Results.Created($"/classes/{result.Id}", result);
        });

        app.MapGet("/classes", async (IClassService svc, CancellationToken ct) =>
        {
            var classes = await svc.GetAllAsync(ct);
            return Results.Ok(classes);
        });

        app.MapGet("/classes/{classId}", async (string classId, IClassService svc, CancellationToken ct) =>
        {
            var cls = await svc.GetByIdAsync(classId, ct);
            return Results.Ok(cls);
        });
    }
}
