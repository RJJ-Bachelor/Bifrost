using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Teacher.GetHelpRequest;

public sealed class GetHelpRequestQueryHandler(HelpRequestAccess access) : ICommandHandler<GetHelpRequestQuery, HelpRequestResult>
{
    public async Task<HelpRequestResult> ExecuteAsync(GetHelpRequestQuery query, CancellationToken cancellationToken) =>
        (await access.ForRequestAsync(query.Id, query.TeacherId, AuthorRole.Teacher, false, cancellationToken)).ToResult();
}
