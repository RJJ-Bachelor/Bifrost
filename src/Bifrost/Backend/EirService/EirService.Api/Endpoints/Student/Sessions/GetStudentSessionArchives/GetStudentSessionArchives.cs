using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.Common;
using EirService.Sessions.Application.Features.Common.GetSessionArchives;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.Sessions.GetStudentSessionArchives;

internal sealed class GetStudentSessionArchives : EndpointWithoutRequest<IReadOnlyList<SessionArchiveSummary>>
{
    public override void Configure()
    {
        Get("/students/sessions/{sessionId:guid}/archives");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(GetStudentSessionArchives)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var query = new GetSessionArchivesQuery(Route<Guid>("sessionId"), User.GetRequiredUserId(), false);
        await Send.OkAsync(await query.ExecuteAsync(cancellationToken), cancellationToken);
    }
}
