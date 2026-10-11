using FastEndpoints;
using FluentValidation;
using EirService.Contracts.Exceptions;

namespace EirService.Api.Middleware;

public sealed class ValidationExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception exception) when (
            exception is ValidationException or ResourceNotFoundException or OperationConflictException &&
            !context.Response.HasStarted &&
            context.GetEndpoint()?.Metadata.GetMetadata<EndpointDefinition>() is not null)
        {
            context.Response.Clear();
            var statusCode = exception switch
            {
                ResourceNotFoundException => StatusCodes.Status404NotFound,
                OperationConflictException => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            context.Response.StatusCode = statusCode;
            if (exception is ValidationException validation)
                await context.Response.WriteAsJsonAsync(new ErrorResponse(validation.Errors.ToList(), statusCode), cancellationToken: context.RequestAborted);
            else
                await context.Response.WriteAsJsonAsync(new { message = exception.Message }, cancellationToken: context.RequestAborted);
        }
    }
}
