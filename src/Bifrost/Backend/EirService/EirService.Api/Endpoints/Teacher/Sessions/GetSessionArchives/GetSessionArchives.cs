using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Features.Common.GetSessionArchives;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Sessions.GetSessionArchives;

internal sealed class GetSessionArchives : EndpointWithoutRequest<IReadOnlyList<SessionArchiveSummary>>
{
    public override void Configure()
    {
        Get("/teachers/sessions/{sessionId:guid}/archives");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(GetSessionArchives)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var query = new GetSessionArchivesQuery(Route<Guid>("sessionId"), User.GetRequiredUserId(), true);
        await Send.OkAsync(await query.ExecuteAsync(cancellationToken), cancellationToken);
    }
}
