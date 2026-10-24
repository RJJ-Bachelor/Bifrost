using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Features.Common.GetSessionArchive;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Sessions.GetSessionArchive;

internal sealed class GetSessionArchive : EndpointWithoutRequest<SessionArchiveResult>
{
    public override void Configure()
    {
        Get("/teachers/sessions/{sessionId:guid}/archives/{archiveId:guid}");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(GetSessionArchive)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var query = new GetSessionArchiveQuery(Route<Guid>("sessionId"), Route<Guid>("archiveId"), User.GetRequiredUserId(), true);
        await Send.OkAsync(await query.ExecuteAsync(cancellationToken), cancellationToken);
    }
}
