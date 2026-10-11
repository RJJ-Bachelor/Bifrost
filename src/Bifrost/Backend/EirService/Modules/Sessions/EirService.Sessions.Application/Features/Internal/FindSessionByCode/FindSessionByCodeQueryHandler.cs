using EirService.Sessions.Domain.ValueObjects;
using EirService.Contracts.Sessions.Common;
using EirService.Contracts.Sessions.FindSessionByCode;
using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;

namespace EirService.Sessions.Application.Features.Internal.FindSessionByCode;

public sealed class FindSessionByCodeQueryHandler(ISessionRepository repository)
    : ICommandHandler<FindSessionByCodeQuery, StudentSessionSummary?>
{
    public async Task<StudentSessionSummary?> ExecuteAsync(FindSessionByCodeQuery query, CancellationToken cancellationToken)
    {
        if (!SessionCode.IsValid(query.Code)) return null;
        return (await repository.GetByCodeAsync(new SessionCode(query.Code).Value, cancellationToken))?.ToStudentSummary();
    }
}
