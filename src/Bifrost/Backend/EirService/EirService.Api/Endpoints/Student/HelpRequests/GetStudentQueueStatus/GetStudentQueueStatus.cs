using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Student.GetStudentQueueStatus;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.HelpRequests.GetStudentQueueStatus;

internal sealed class GetStudentQueueStatus : EndpointWithoutRequest<StudentQueueStatusResult>
{
    public override void Configure()
    {
        Get("/students/sessions/{sessionId:guid}/queue/status");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(GetStudentQueueStatus)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await new GetStudentQueueStatusQuery(Route<Guid>("sessionId"), User.GetRequiredUserId()).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
