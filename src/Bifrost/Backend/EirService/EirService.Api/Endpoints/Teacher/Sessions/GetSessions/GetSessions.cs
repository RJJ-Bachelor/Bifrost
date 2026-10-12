using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.Teacher.GetSessions;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Sessions.GetSessions;

internal sealed class GetSessions : EndpointWithoutRequest<IReadOnlyList<SessionResult>>
{
    public override void Configure()
    {
        Get("/teachers/sessions");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(GetSessions)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var query = new GetSessionsQuery(User.GetRequiredUserId());
        var result = await query.ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
