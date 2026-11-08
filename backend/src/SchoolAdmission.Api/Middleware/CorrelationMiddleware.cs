using System.Diagnostics;

namespace SchoolAdmission.Api.Middleware;

public class CorrelationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationMiddleware> _logger;

    public CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(requestId))
        {
            requestId = Guid.NewGuid().ToString("N");
        }

        context.TraceIdentifier = requestId;
        context.Response.Headers["X-Request-Id"] = requestId;

        var sw = Stopwatch.StartNew();
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["requestId"] = requestId,
            ["traceId"] = requestId,
            ["path"] = context.Request.Path.Value ?? string.Empty,
            ["method"] = context.Request.Method
        }))
        {
            await _next(context);
            sw.Stop();

            if (!context.Request.Path.StartsWithSegments("/health") && !context.Request.Path.StartsWithSegments("/metrics"))
            {
                _logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {DurationMs:0.0}ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    sw.Elapsed.TotalMilliseconds);
            }
        }
    }
}
