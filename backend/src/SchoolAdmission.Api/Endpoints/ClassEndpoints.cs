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
            ClassService svc) =>
        {
            if (req is null)
                throw AppException.ValidationError("Request body is required.");
            var result = await svc.CreateAsync(req);
            return Results.Created($"/classes/{result.Id}", result);
        });

        app.MapGet("/classes", async (ClassService svc) =>
        {
            var classes = await svc.GetAllAsync();
            return Results.Ok(classes);
        });

        app.MapGet("/classes/{classId}", async (string classId, ClassService svc) =>
        {
            var cls = await svc.GetByIdAsync(classId);
            return Results.Ok(cls);
        });
    }
}
