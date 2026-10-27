using EirService.Api.Authentication;
using EirService.Participants.Application.Features.Teacher.JoinSession;
using EirService.Sessions.Application.Features.Teacher.Common;
using EirService.Sessions.Application.Features.Teacher.GetSession;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Participants.JoinSession;

internal sealed class JoinTeacherSession : Endpoint<JoinTeacherSessionRequest, SessionResult>
{
    public override void Configure()
    {
        Post("/teachers/sessions/join");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(JoinTeacherSession)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(JoinTeacherSessionRequest request, CancellationToken cancellationToken)
    {
        var teacherId = User.GetRequiredUserId();
        var sessionId = await new JoinTeacherSessionCommand(request.Code, teacherId, User.Identity?.Name).ExecuteAsync(cancellationToken);
        var result = await new GetSessionQuery(sessionId, teacherId).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
