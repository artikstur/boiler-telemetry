using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FluentValidation;
using Serilog.Context;

namespace BoilerTelemetry.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);

            sw.Stop();
            if (context.Response.StatusCode is >= 400 and < 500)
            {
                using (LogContext.PushProperty("StatusCode", context.Response.StatusCode))
                using (LogContext.PushProperty("Method", context.Request.Method))
                using (LogContext.PushProperty("Path", context.Request.Path.Value))
                using (LogContext.PushProperty("ElapsedMs", sw.ElapsedMilliseconds))
                {
                    _logger.LogWarning("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                        context.Request.Method, context.Request.Path, context.Response.StatusCode, sw.ElapsedMilliseconds);
                }
            }
        }
        catch (ValidationException ex)
        {
            sw.Stop();
            var errors = ex.Errors.Select(e => e.ErrorMessage).ToArray();
            using (LogContext.PushProperty("ValidationErrors", errors, destructureObjects: true))
            using (LogContext.PushProperty("Method", context.Request.Method))
            using (LogContext.PushProperty("Path", context.Request.Path.Value))
            using (LogContext.PushProperty("ElapsedMs", sw.ElapsedMilliseconds))
            {
                _logger.LogWarning("Validation failed for {Method} {Path}: {ValidationErrors}",
                    context.Request.Method, context.Request.Path, errors);
            }
            await WriteErrorResponse(context, HttpStatusCode.BadRequest, errors);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            sw.Stop();
            using (LogContext.PushProperty("Method", context.Request.Method))
            using (LogContext.PushProperty("Path", context.Request.Path.Value))
            {
                _logger.LogWarning(ex, "Conflict on {Method} {Path}: {Message}",
                    context.Request.Method, context.Request.Path, ex.Message);
            }
            await WriteErrorResponse(context, HttpStatusCode.Conflict, [ex.Message]);
        }
        catch (Exception ex)
        {
            sw.Stop();
            using (LogContext.PushProperty("Method", context.Request.Method))
            using (LogContext.PushProperty("Path", context.Request.Path.Value))
            using (LogContext.PushProperty("ElapsedMs", sw.ElapsedMilliseconds))
            {
                _logger.LogError(ex, "Unhandled exception on {Method} {Path} after {ElapsedMs}ms",
                    context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds);
            }
            await WriteErrorResponse(context, HttpStatusCode.InternalServerError,
                ["An internal error occurred"]);
        }
    }

    private static async Task WriteErrorResponse(HttpContext context, HttpStatusCode status, string[] errors)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new { errors });
        await context.Response.WriteAsync(body);
    }
}
