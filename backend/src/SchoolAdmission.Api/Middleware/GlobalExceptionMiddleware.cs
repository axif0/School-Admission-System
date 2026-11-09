using System.Text.Json;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Api.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("Client aborted request on {Path}", context.Request.Path);
        }
        catch (AppException ex)
        {
            _logger.LogWarning("AppException: {Code} {Message}", ex.Code, ex.Message);
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/json";
            var body = new ErrorResponse(new ErrorDetail(ex.Code, ex.Message, context.TraceIdentifier));
            await context.Response.WriteAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
        catch (Exception ex) when (PostgresErrorClassifier.IsBusy(ex, context.RequestAborted))
        {
            _logger.LogWarning("DB overload/busy: {Message}", ex.Message);
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/json";
            var body = new ErrorResponse(new ErrorDetail("TEMPORARILY_BUSY",
                "The system is temporarily busy. Please try again in a moment.", context.TraceIdentifier));
            await context.Response.WriteAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var body = new ErrorResponse(new ErrorDetail("INTERNAL_ERROR",
                "An unexpected error occurred. Please try again later.", context.TraceIdentifier));
            await context.Response.WriteAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    }
}
