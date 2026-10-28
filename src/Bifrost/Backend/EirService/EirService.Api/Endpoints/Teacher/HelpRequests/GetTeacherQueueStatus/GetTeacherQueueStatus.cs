using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Teacher.GetTeacherQueueStatus;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.HelpRequests.GetTeacherQueueStatus;

internal sealed class GetTeacherQueueStatus : EndpointWithoutRequest<TeacherQueueStatusResult>
{
    public override void Configure()
    {
        Get("/teachers/sessions/{sessionId:guid}/queue/status");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(GetTeacherQueueStatus)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await new GetTeacherQueueStatusQuery(Route<Guid>("sessionId"), User.GetRequiredUserId()).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
