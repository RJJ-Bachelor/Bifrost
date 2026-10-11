using EirService.Sessions.Application.Features.Common;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;

namespace EirService.Sessions.Application.Features.CreateSession;

public sealed class CreateSessionCommandHandler(
    ISessionRepository repository, TimeProvider timeProvider, ILogger<CreateSessionCommandHandler> logger)
    : ICommandHandler<CreateSessionCommand, SessionResult>
{
    public async Task<SessionResult> ExecuteAsync(CreateSessionCommand request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow();
        // A unique index arbitrates collisions, including concurrent creators.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var session = Session.Create(request.Title, SessionCode.Generate(), request.TeacherId, request.DisplayName, now);
            if (await repository.TryAddAsync(session, cancellationToken))
            {
                logger.LogInformation("CreateSession handler called for teacher {TeacherId}", request.TeacherId);
                return SessionResult.From(session);
            }
        }

        throw new InvalidOperationException("A unique session code could not be allocated.");
    }
}
