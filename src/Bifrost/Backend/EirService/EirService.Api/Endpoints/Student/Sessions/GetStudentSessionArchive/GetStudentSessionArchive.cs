using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Features.Common.GetSessionArchive;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.Sessions.GetStudentSessionArchive;

internal sealed class GetStudentSessionArchive : EndpointWithoutRequest<SessionArchiveResult>
{
    public override void Configure()
    {
        Get("/students/sessions/{sessionId:guid}/archives/{archiveId:guid}");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(GetStudentSessionArchive)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var query = new GetSessionArchiveQuery(Route<Guid>("sessionId"), Route<Guid>("archiveId"), User.GetRequiredUserId(), false);
        await Send.OkAsync(await query.ExecuteAsync(cancellationToken), cancellationToken);
    }
}
