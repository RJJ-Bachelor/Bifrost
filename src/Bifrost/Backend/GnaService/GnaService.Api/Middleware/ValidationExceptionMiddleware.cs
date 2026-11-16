using FastEndpoints;
using FluentValidation;

namespace GnaService.Api.Middleware;

public sealed class ValidationExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (ValidationException exception) when (!context.Response.HasStarted &&
            context.GetEndpoint()?.Metadata.GetMetadata<EndpointDefinition>() is not null)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ErrorResponse(exception.Errors.ToList(), StatusCodes.Status400BadRequest),
                cancellationToken: context.RequestAborted);
        }
    }
}
