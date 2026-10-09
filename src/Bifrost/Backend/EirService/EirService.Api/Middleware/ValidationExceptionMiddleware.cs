using FastEndpoints;
using FluentValidation;

namespace EirService.Api.Middleware;

public sealed class ValidationExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException exception) when (
            !context.Response.HasStarted &&
            context.GetEndpoint()?.Metadata.GetMetadata<EndpointDefinition>() is not null)
        {
            // Limit this experiment's error translation to FastEndpoints.
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            var response = new ErrorResponse(exception.Errors.ToList(), StatusCodes.Status400BadRequest);
            await context.Response.WriteAsJsonAsync(response, cancellationToken: context.RequestAborted);
        }
    }
}
