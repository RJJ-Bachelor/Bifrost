using EirService.Contracts.HelpRequests.CanApplyGeneralization;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Repositories;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Internal.CanApplyGeneralization;

public sealed class CanApplyGeneralizationQueryHandler(IHelpRequestRepository repository)
    : ICommandHandler<CanApplyGeneralizationQuery, bool>
{
    public async Task<bool> ExecuteAsync(CanApplyGeneralizationQuery query, CancellationToken cancellationToken) =>
        await GeneralizationTargets.FindAsync(repository, query.GeneralizationId, query.SessionId,
            query.Generation, query.HelpRequestIds, cancellationToken) is not null;
}
