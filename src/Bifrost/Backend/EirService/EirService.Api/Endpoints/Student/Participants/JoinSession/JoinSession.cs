using EirService.Contracts.Sessions.Common;
using EirService.Api.Authentication;
using EirService.Participants.Application.Features.Student.JoinSession;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.Participants.JoinSession;

internal sealed class JoinSession : Endpoint<JoinSessionRequest, SessionSummary>
{
    public override void Configure()
    {
        Post("/students/sessions/join");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(JoinSession)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(JoinSessionRequest request, CancellationToken cancellationToken)
    {
        var command = new JoinSessionCommand(request.Code, User.GetRequiredUserId());
        var result = await command.ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
