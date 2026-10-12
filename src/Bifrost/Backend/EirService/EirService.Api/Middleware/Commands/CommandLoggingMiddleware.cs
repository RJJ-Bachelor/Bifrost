using FastEndpoints;

namespace EirService.Api.Middleware.Commands;

public sealed class CommandLoggingMiddleware<TCommand, TResult>(
    ILogger<CommandLoggingMiddleware<TCommand, TResult>> logger)
    : ICommandMiddleware<TCommand, TResult> where TCommand : ICommand<TResult>
{
    public async Task<TResult> ExecuteAsync(
        TCommand command,
        CommandDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Executing command {CommandName}", typeof(TCommand).Name);

        var result = await next();

        logger.LogInformation("Executed command {CommandName}", typeof(TCommand).Name);
        return result;
    }
}
