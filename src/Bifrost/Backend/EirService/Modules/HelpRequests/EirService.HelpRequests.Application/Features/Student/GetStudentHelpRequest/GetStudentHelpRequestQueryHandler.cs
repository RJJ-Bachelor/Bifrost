using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;

namespace EirService.HelpRequests.Application.Features.Student.GetStudentHelpRequest;

public sealed class GetStudentHelpRequestQueryHandler(HelpRequestAccess access) : ICommandHandler<GetStudentHelpRequestQuery, HelpRequestResult>
{
    public async Task<HelpRequestResult> ExecuteAsync(GetStudentHelpRequestQuery query, CancellationToken cancellationToken) =>
        await (await access.ForRequestAsync(query.Id, query.StudentId, AuthorRole.Student, false, cancellationToken)).ToResultAsync(cancellationToken);
}
