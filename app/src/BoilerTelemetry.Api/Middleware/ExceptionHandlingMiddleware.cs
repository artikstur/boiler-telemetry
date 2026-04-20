using System.Net;
using System.Text.Json;
using FluentValidation;

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
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            await WriteErrorResponse(context, HttpStatusCode.BadRequest,
                ex.Errors.Select(e => e.ErrorMessage).ToArray());
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            await WriteErrorResponse(context, HttpStatusCode.Conflict, [ex.Message]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
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
