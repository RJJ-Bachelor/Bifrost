using FastEndpoints;
using FluentValidation;

namespace EirService.Api.Middleware.Commands;

public sealed class CommandValidationMiddleware<TCommand, TResult>(IEnumerable<IValidator<TCommand>> validators)
    : ICommandMiddleware<TCommand, TResult> where TCommand : ICommand<TResult>
{
    public async Task<TResult> ExecuteAsync(
        TCommand command,
        CommandDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var results = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(command, cancellationToken)));
        var failures = results.SelectMany(result => result.Errors).ToArray();

        if (failures.Length > 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
