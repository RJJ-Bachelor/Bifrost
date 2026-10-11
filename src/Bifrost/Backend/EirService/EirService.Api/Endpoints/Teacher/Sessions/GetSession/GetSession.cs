using EirService.Sessions.Application.Features.Common;
using EirService.Api.Authentication;
using EirService.Sessions.Application.Features.GetSession;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Sessions.GetSession;

internal sealed class GetSession : EndpointWithoutRequest<SessionResult>
{
    public override void Configure()
    {
        Get("/teachers/sessions/{sessionId:guid}");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(GetSession)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var query = new GetSessionQuery(Route<Guid>("sessionId"), User.GetRequiredUserId());
        var result = await query.ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
