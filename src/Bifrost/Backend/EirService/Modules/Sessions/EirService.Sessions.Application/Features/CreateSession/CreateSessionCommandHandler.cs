using FastEndpoints;
using Microsoft.Extensions.Logging;

namespace EirService.Sessions.Application.Features.CreateSession;

public sealed class CreateSessionCommandHandler(ILogger<CreateSessionCommandHandler> logger)
    : ICommandHandler<CreateSessionCommand, bool>
{
    public Task<bool> ExecuteAsync(CreateSessionCommand request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation("CreateSession handler called for teacher {TeacherId}", request.TeacherId);

        return Task.FromResult(true);
    }
}
